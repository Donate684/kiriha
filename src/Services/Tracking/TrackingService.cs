using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Models.Messages;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Tracking;

public class TrackingService : IDisposable
{
    private readonly AnisthesiaService _anisthesiaService;
    private readonly MappingService _mappingService;
    private readonly AnimeRepository _animeRepo;
    private readonly SettingsService _settingsService;
    private readonly DiscordService _discordService;
    private readonly IScrobbleService _scrobbleService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IEnumerable<ITrackerService> _trackers;

    // _state guards _currentMedia and _matchedAnime which are read/written from the
    // Anisthesia background thread (MediaDetected/MediaCleared) and from UI command handlers.
    private readonly object _state = new();
    private ParsedMedia? _currentMedia;
    private AnimeItem? _matchedAnime;
    private bool _manualMapInProgress;

    public ParsedMedia? CurrentMedia { get { lock (_state) return _currentMedia; } }
    public AnimeItem? MatchedAnime { get { lock (_state) return _matchedAnime; } }

    public TrackingService(
        AnisthesiaService anisthesiaService,
        MappingService mappingService,
        AnimeRepository animeRepo,
        SettingsService settingsService,
        DiscordService discordService,
        IScrobbleService scrobbleService,
        IUiDispatcher uiDispatcher,
        IEnumerable<ITrackerService> trackers)
    {
        _anisthesiaService = anisthesiaService;
        _mappingService = mappingService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;
        _discordService = discordService;
        _scrobbleService = scrobbleService;
        _uiDispatcher = uiDispatcher;
        _trackers = trackers;

        _anisthesiaService.MediaDetected += OnMediaDetected;
        _anisthesiaService.MediaCleared += OnMediaCleared;
        _scrobbleService.CountdownUpdated += OnScrobbleCountdownUpdated;
    }

    private void OnScrobbleCountdownUpdated(object? sender, string e)
    {
        _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new TrackingCountdownMessage(e)));
    }

    public async Task ManualMapAsync(int animeId)
    {
        ParsedMedia? media;
        lock (_state)
        {
            media = _currentMedia;
            if (media == null) return;
            _manualMapInProgress = true;
        }

        try
        {
            Log.Information("TrackingService: Manually mapping '{Title}' to ID {Id}", media.AnimeTitle, animeId);
            _mappingService.AddMapping(media.AnimeTitle, animeId);

            // Use a temporary flag or bypass to ensure it works even if scrobbler is disabled
            await MatchMediaAsync(media, forceMatch: true);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public void SetInternalMedia(InternalPlayerState state)
    {
        if (state.IsClosed)
        {
            ParsedMedia? current;
            lock (_state) current = _currentMedia;
            if (current != null && current.ProcessName == "KirihaInternal")
            {
                OnMediaCleared(this, EventArgs.Empty);
            }
            return;
        }

        var media = new ParsedMedia
        {
            ProcessName = "KirihaInternal",
            OriginalTitle = !string.IsNullOrEmpty(state.OriginalTitle) ? state.OriginalTitle : state.AnimeTitle,
            AnimeTitle = state.AnimeTitle,
            Episode = state.Episode,
            Position = TimeSpan.FromSeconds(state.Position),
            Duration = TimeSpan.FromSeconds(state.Duration),
            IsPlaying = state.IsPlaying,
            Pid = 0
        };

        if (state.AnimeId.HasValue)
        {
            _ = SetMatchedInternalMedia(media, state.AnimeId.Value);
        }
        else
        {
            _ = MatchMediaAsync(media);
        }
    }

    private async Task SetMatchedInternalMedia(ParsedMedia media, int animeId)
    {
        try
        {
            ParsedMedia? prev;
            lock (_state) prev = _currentMedia;
            if (prev != null && prev.AnimeTitle == media.AnimeTitle && prev.Episode == media.Episode)
            {
                bool timeLeap = false;
                if (prev.Position.HasValue && media.Position.HasValue)
                {
                    timeLeap = Math.Abs((media.Position.Value - prev.Position.Value).TotalSeconds) > 3;
                }

                bool changed = prev.IsPlaying != media.IsPlaying || prev.ProcessName != media.ProcessName || timeLeap;
                lock (_state) _currentMedia = media;

                if (!changed)
                {
                    return;
                }

                _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new MediaChangedMessage(media)));
                _scrobbleService.UpdatePlayingState(media.IsPlaying);

                // We need to update presence periodically for position/duration changes if we seek, 
                // but for smooth ticking Discord handles it. We just call it if IsPlaying changed, or significant time leap.
                AnimeItem? currentMatched;
                lock (_state) currentMatched = _matchedAnime;

                if (currentMatched != null)
                {
                    NotifyPlayerMetadata(media, currentMatched);
                    UpdateDiscordPresence(media, currentMatched);
                }

                return;
            }

            lock (_state)
            {
                _currentMedia = media;
                _matchedAnime = null;
            }
            _scrobbleService.CancelScrobble();

            _uiDispatcher.Post(() =>
            {
                WeakReferenceMessenger.Default.Send(new MediaChangedMessage(media));
                WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(null));
            });

            await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(5000));

            var userList = await _uiDispatcher.InvokeAsync(() => _animeRepo.Collection.ToList());
            var matched = userList.FirstOrDefault(x => x.Id == animeId);

            if (matched == null)
            {
                var activeTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
                if (activeTracker != null)
                {
                    try
                    {
                        var fetched = await activeTracker.GetAnimeDetailsAsync(animeId);
                        if (fetched != null)
                        {
                            matched = fetched;
                            matched.Status = UserAnimeStatus.None; // Ensure it's not scrobbled or auto-added incorrectly
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to fetch anime details for ID {AnimeId}", animeId);
                    }
                }
            }

            ParsedMedia? cur;
            lock (_state) cur = _currentMedia;
            if (!IsSameMedia(cur, media)) return;

            if (matched != null)
            {
                if (matched.TotalEpisodes <= 1 && string.IsNullOrWhiteSpace(media.Episode))
                {
                    media.Episode = "1";
                }

                NotifyPlayerMetadata(media, matched);

                bool isValid;
                lock (_state)
                {
                    isValid = IsSameMedia(_currentMedia, media);
                    if (isValid)
                    {
                        _matchedAnime = matched;
                    }
                }

                if (isValid)
                {
                    _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(matched)));

                    UpdateDiscordPresence(media, matched);

                    if (matched.Status != UserAnimeStatus.None)
                        _scrobbleService.StartScrobble(media, matched);
                }
            }
            else
            {
                _discordService.UpdatePresence(media.AnimeTitle, media.Episode, 0, null, null, media.Position, media.Duration, null, media.IsPlaying);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during tracking mapping for internal player");
        }
    }

    public async Task RemoveManualMappingAsync()
    {
        ParsedMedia? media;
        lock (_state)
        {
            media = _currentMedia;
            if (media == null) return;
            _manualMapInProgress = true;
        }

        try
        {
            Log.Information("TrackingService: Removing manual mapping for '{Title}'", media.AnimeTitle);
            _mappingService.RemoveMapping(media.AnimeTitle);

            await MatchMediaAsync(media, forceMatch: true);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public async Task AddNegativeMappingAsync()
    {
        ParsedMedia? media;
        lock (_state)
        {
            media = _currentMedia;
            if (media == null) return;
            _manualMapInProgress = true;
        }

        try
        {
            Log.Information("TrackingService: Adding negative mapping for '{Title}'", media.AnimeTitle);
            _mappingService.AddNegativeMapping(media.AnimeTitle);

            await MatchMediaAsync(media, forceMatch: true);
        }
        finally
        {
            lock (_state) _manualMapInProgress = false;
        }
    }

    public bool IsManuallyMapped()
    {
        ParsedMedia? media;
        lock (_state) media = _currentMedia;
        if (media == null) return false;
        return _mappingService.IsManuallyMapped(media.AnimeTitle) ||
               _mappingService.IsManuallyMapped(media.OriginalTitle);
    }

    private void OnMediaCleared(object? sender, EventArgs e)
    {
        lock (_state)
        {
            _currentMedia = null;
            _matchedAnime = null;
        }
        _scrobbleService.CancelScrobble();

        _uiDispatcher.Post(() =>
        {
            WeakReferenceMessenger.Default.Send(new MediaChangedMessage(null));
            WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(null));
        });
        _discordService.ClearPresence();
    }

    private async void OnMediaDetected(object? sender, ParsedMedia media)
    {
        // async void event handler: any leaked exception bubbles to the
        // synchronization context (here: process-wide) and tears the app down.
        // The handler is invoked from AnisthesiaService's polling thread, so
        // we can't rely on a UI-thread safety net either.
        try
        {
            if (!_settingsService.Current.System.Scrobbler.Enabled) return;

            lock (_state)
            {
                if (_manualMapInProgress) return;
            }

            await MatchMediaAsync(media);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex) { Log.Error(ex, "TrackingService: OnMediaDetected failed for {Title}", media?.AnimeTitle); }
    }

    private async Task MatchMediaAsync(ParsedMedia media, bool forceMatch = false)
    {
        // Check if it's the same media
        ParsedMedia? prev;
        lock (_state) prev = _currentMedia;
        if (!forceMatch && prev != null && prev.AnimeTitle == media.AnimeTitle && prev.Episode == media.Episode)
        {
            bool changed = prev.IsPlaying != media.IsPlaying || prev.ProcessName != media.ProcessName;
            lock (_state) _currentMedia = media;
            if (changed)
            {
                _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new MediaChangedMessage(media)));
                _scrobbleService.UpdatePlayingState(media.IsPlaying);
            }
            return;
        }

        lock (_state)
        {
            _currentMedia = media;
            _matchedAnime = null;
        }
        _scrobbleService.CancelScrobble();

        _uiDispatcher.Post(() =>
        {
            WeakReferenceMessenger.Default.Send(new MediaChangedMessage(media));
            WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(null)); // Clear previous match UI immediately
            WeakReferenceMessenger.Default.Send(new TrackingStatusMessage(UIUtils.GetLoc("scrobbler.status.matching")));
        });

        try
        {
            // Wait for services to be ready (e.g. at app startup)
            await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(5000));

            // Respect user's explicit unlink: skip all mapping attempts.
            if (_mappingService.IsNegativelyMapped(media.OriginalTitle) ||
                _mappingService.IsNegativelyMapped(media.AnimeTitle))
            {
                Log.Information("TrackingService: '{Title}' is negatively mapped, skipping auto-match", media.AnimeTitle);
                return;
            }

            // Snapshot the user list on UI thread — ObservableCollection is not thread-safe
            // and MappingService enumerates it lazily multiple times.
            var userList = await _uiDispatcher.InvokeAsync(
                () => _animeRepo.Collection.ToList());

            // Perform Mapping
            int? malId = await _mappingService.GetIdFromTitleAsync(media.OriginalTitle, userList);

            // Race: another media event may have arrived while we were mapping.
            ParsedMedia? cur;
            lock (_state) cur = _currentMedia;
            if (!IsSameMedia(cur, media)) return;

            if (!malId.HasValue)
            {
                malId = await _mappingService.SearchOnMalAsync(media.OriginalTitle);
                lock (_state) cur = _currentMedia;
                if (!IsSameMedia(cur, media)) return;
            }

            if (malId.HasValue)
            {
                var matched = userList.FirstOrDefault(x => x.Id == malId.Value);

                if (matched == null)
                {
                    var activeTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
                    if (activeTracker != null)
                    {
                        try
                        {
                            var fetched = await activeTracker.GetAnimeDetailsAsync(malId.Value);
                            if (fetched != null)
                            {
                                matched = fetched;
                                matched.Status = UserAnimeStatus.None; // Ensure it's not scrobbled or auto-added incorrectly
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to fetch anime details for ID {AnimeId}", malId.Value);
                        }
                    }
                }

                if (matched != null && matched.TotalEpisodes <= 1 && string.IsNullOrWhiteSpace(media.Episode))
                {
                    media.Episode = "1";
                }

                bool isValid;
                lock (_state)
                {
                    isValid = IsSameMedia(_currentMedia, media);
                    if (isValid)
                    {
                        _matchedAnime = matched;
                    }
                }

                if (!isValid) return;

                _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(matched)));

                if (matched != null)
                {
                    NotifyPlayerMetadata(media, matched);

                    UpdateDiscordPresence(media, matched);

                    if (matched.Status != UserAnimeStatus.None)
                        _scrobbleService.StartScrobble(media, matched);
                }
                else
                {
                    _discordService.UpdatePresence(media.AnimeTitle, media.Episode, 0, null, null, media.Position, media.Duration, null, media.IsPlaying);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during tracking mapping");
        }
        finally
        {
            ParsedMedia? cur;
            lock (_state) cur = _currentMedia;
            if (IsSameMedia(cur, media))
            {
                _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new TrackingStatusMessage(string.Empty)));
            }
        }
    }

    private static bool IsSameMedia(ParsedMedia? a, ParsedMedia? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        return a.AnimeTitle == b.AnimeTitle && a.Episode == b.Episode;
    }

    public void Dispose()
    {
        _anisthesiaService.MediaDetected -= OnMediaDetected;
        _anisthesiaService.MediaCleared -= OnMediaCleared;
        _scrobbleService.CountdownUpdated -= OnScrobbleCountdownUpdated;
        _scrobbleService.CancelScrobble();
    }

    private static void NotifyPlayerMetadata(ParsedMedia media, AnimeItem matched)
    {
        if (!string.Equals(media.ProcessName, "KirihaInternal", StringComparison.Ordinal))
            return;

        PlayerProcessBridge.ForwardMetadata(
            media.OriginalTitle,
            matched.Id,
            matched.RussianTitle ?? matched.Title,
            matched.EnglishTitle ?? matched.Title,
            media.Episode);
    }

    private void UpdateDiscordPresence(ParsedMedia media, AnimeItem matched)
    {
        string? mainTitle = matched.Title ?? matched.EnglishTitle;
        string? subTitle = matched.RussianTitle;

        string discordTitle = (!string.IsNullOrEmpty(subTitle) && !string.IsNullOrEmpty(mainTitle) && subTitle != mainTitle)
            ? $"{mainTitle} | {subTitle}"
            : (!string.IsNullOrEmpty(subTitle) ? subTitle : (mainTitle ?? "Anime"));

        string malUrl = $"https://myanimelist.net/anime/{matched.Id}";
        string shikiUrl = $"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{matched.Id}";

        _discordService.UpdatePresence(discordTitle, media.Episode, matched.TotalEpisodes, malUrl, shikiUrl, media.Position, media.Duration, matched.MainPictureUrl, media.IsPlaying);
    }
}
