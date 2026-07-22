using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Kiriha.Models.Messages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using Serilog;

namespace Kiriha.ViewModels;

public partial class NowPlayingViewModel : ViewModelBase, IDisposable,
    IRecipient<MediaChangedMessage>,
    IRecipient<AnimeMatchedMessage>,
    IRecipient<TrackingCountdownMessage>,
    IRecipient<TrackingStatusMessage>
{
    private readonly TrackingService _trackingService;
    // Tracks the anime id of an in-flight manual selection. Until the background
    // TrackingService fires AnimeMatched with this id (or null on a media change),
    // we ignore intermediate null/other matches so they don't clobber the UI choice.
    // 0 means "no manual selection pending".
    private int _pendingManualMatchId;
    private readonly SettingsService _settingsService;
    private readonly MappingService _mappingService;
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeProgressService _progressService;
    private readonly SyncManager _syncManager;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly Services.Api.MalApiService _malApi;

    [ObservableProperty] private ParsedMedia? _currentMedia;
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsNotInList))]
    [NotifyPropertyChangedFor(nameof(AllAlternativeTitles))]
    [NotifyPropertyChangedFor(nameof(HasAlternativeTitles))]
    private AnimeItem? _matchedAnime;
    [ObservableProperty] private AnimeItem? _pendingMatch;

    /// <summary>
    /// Resolved user-defined share buttons for the currently matched anime.
    /// Refreshed via <see cref="OnMatchedAnimeChanged"/>.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    partial void OnMatchedAnimeChanged(AnimeItem? value)
    {
        CustomShareLinks.Clear();
        if (value == null) return;
        foreach (var link in _settingsService.Current.CustomLinks)
        {
            if (string.IsNullOrWhiteSpace(link.UrlTemplate)) continue;
            var url = Kiriha.Core.CustomLinkResolver.Resolve(link.UrlTemplate, value);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isManuallyMapped;

    public bool IsNotInList => MatchedAnime != null && MatchedAnime.Status == UserAnimeStatus.None;
    
    public System.Collections.Generic.IEnumerable<string> AllAlternativeTitles
    {
        get
        {
            var list = new System.Collections.Generic.List<string>();
            if (MatchedAnime == null) return list;

            if (!string.IsNullOrEmpty(MatchedAnime.EnglishTitle) && MatchedAnime.EnglishTitle != MatchedAnime.Title) 
                list.Add(MatchedAnime.EnglishTitle);
            if (!string.IsNullOrEmpty(MatchedAnime.JapaneseTitle) && MatchedAnime.JapaneseTitle != MatchedAnime.Title) 
                list.Add(MatchedAnime.JapaneseTitle);
            
            foreach (var syn in MatchedAnime.AlternativeTitles)
            {
                if (syn != MatchedAnime.Title && !list.Contains(syn))
                    list.Add(syn);
            }
            return list;
        }
    }

    public bool HasAlternativeTitles => AllAlternativeTitles.Any();
    
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _isMediaDetected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _isPaused;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string _countdownStatus = string.Empty;

    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string _trackingStatus = string.Empty;

    public string DisplayStatus => !IsMediaDetected ? UIUtils.GetLoc("scrobbler.status.ready") : 
                                   (!string.IsNullOrEmpty(TrackingStatus) ? TrackingStatus :
                                   (IsPaused ? UIUtils.GetLoc("scrobbler.status.paused") : 
                                   (string.IsNullOrEmpty(CountdownStatus) ? UIUtils.GetLoc("scrobbler.status.active") : CountdownStatus)));

    public SettingsService Settings => _settingsService;
    public bool IsScrobblerEnabled => _settingsService.Current.System.Scrobbler.Enabled;

    public ObservableCollection<string> DetectionLogs { get; } = new();
    public ObservableCollection<AnimeItem> Suggestions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    private bool _showSuggestions;

    public bool HasSuggestions => ShowSuggestions && Suggestions.Count > 0;

    [ObservableProperty] private bool _isSearchPanelOpen;

    private CancellationTokenSource? _searchCts;
    private readonly CancellationTokenSource _disposeCts = new();

    public NowPlayingViewModel(
        TrackingService trackingService, 
        SettingsService settingsService, 
        MappingService mappingService, 
        AnimeRepository animeRepo,
        AnimeProgressService progressService,
        SyncManager syncManager,
        ShikiMetadataService shikiMetadataService,
        Services.Api.MalApiService malApi)
    {
        _trackingService = trackingService;
        _settingsService = settingsService;
        _mappingService = mappingService;
        _animeRepo = animeRepo;
        _progressService = progressService;
        _syncManager = syncManager;
        _shikiMetadataService = shikiMetadataService;
        _malApi = malApi;
        
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Sync initial state if any
        CurrentMedia = _trackingService.CurrentMedia;
        MatchedAnime = _trackingService.MatchedAnime;
        IsMediaDetected = CurrentMedia != null;
    }

    [RelayCommand]
    private async Task AddToWatching()
    {
        if (MatchedAnime == null) return;

        try
        {
            if (await _progressService.UpdateProgressAsync(MatchedAnime, MatchedAnime.Progress, UserAnimeStatus.Watching))
            {
                await _animeRepo.AddOrUpdateAnimeAsync(MatchedAnime);
                WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            }

            OnPropertyChanged(nameof(IsNotInList));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add anime to watching");
        }
    }

    [RelayCommand]
    private async Task SelectSuggestion(object parameter)
    {
        if (parameter is not AnimeItem suggestion) return;

        Log.Information("Selecting anime suggestion: {Title} (ID: {Id})", suggestion.Title, suggestion.Id);
        LogDetection(CurrentMedia ?? new ParsedMedia { AnimeTitle = suggestion.Title }, UIUtils.GetLoc("scrobbler.status.mapped_by") + " " + suggestion.DisplayTitle);

        Volatile.Write(ref _pendingManualMatchId, suggestion.Id);
        ShowSuggestions = false;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));

        try
        {
            MatchedAnime = suggestion;
            IsManuallyMapped = true;
            await _trackingService.ManualMapAsync(suggestion.Id);
            // Ensure it stays set — background AnimeMatched will eventually arrive
            // with the same id and clear _pendingManualMatchId from OnAnimeMatched.
            MatchedAnime = suggestion;
            IsManuallyMapped = true;
        }
        catch
        {
            // On error, drop the pending guard so the UI isn't permanently stuck.
            Volatile.Write(ref _pendingManualMatchId, 0);
            throw;
        }
    }

    [RelayCommand]
    private void DismissSuggestions()
    {
        ShowSuggestions = false;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
    }

    [RelayCommand]
    private async Task SearchSuggestions()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        var cts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _searchCts, cts);
        try { oldCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling search CTS"); }
        oldCts?.Dispose();

        IsSearching = true;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, _disposeCts.Token);
            var results = await _malApi.SearchAnimeAsync(SearchQuery, linkedCts.Token);
            if (linkedCts.Token.IsCancellationRequested) return;

            Suggestions.Clear();

            foreach (var r in results)
            {
                Suggestions.Add(r);
            }

            ShowSuggestions = Suggestions.Count > 0;
            OnPropertyChanged(nameof(HasSuggestions));
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to search anime inline");
        }
        finally
        {
            if (_searchCts == cts)
                IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task ManualMatch()
    {
        if (CurrentMedia == null) return;
        
        SearchQuery = CurrentMedia.AnimeTitle;
        await SearchSuggestions();
    }

    [RelayCommand]
    private async Task OpenSearchPanel()
    {
        IsSearchPanelOpen = true;
        if (string.IsNullOrWhiteSpace(SearchQuery) && CurrentMedia != null)
            SearchQuery = CurrentMedia.AnimeTitle;
        if (Suggestions.Count == 0 && !string.IsNullOrWhiteSpace(SearchQuery))
            await SearchSuggestions();
    }

    [RelayCommand]
    private void CloseSearchPanel()
    {
        IsSearchPanelOpen = false;
    }

    public void Receive(TrackingStatusMessage message)
    {
        TrackingStatus = message.Status;
    }

    public void Receive(TrackingCountdownMessage message)
    {
        CountdownStatus = message.Countdown;
    }

    public void Receive(AnimeMatchedMessage message)
    {
        var anime = message.Anime;
        // Suppress intermediate events while a manual selection is in flight.
        // - null: a transient "clearing previous match" event before MappingService re-resolves
        // - different id: stale background match for a previous media — would clobber UI choice
        // We let through the matching id so we can clear the pending guard below.
        var pending = Volatile.Read(ref _pendingManualMatchId);
        if (pending != 0 && (anime == null || anime.Id != pending)) return;
        if (pending != 0 && anime != null && anime.Id == pending)
        {
            Interlocked.CompareExchange(ref _pendingManualMatchId, 0, pending);
        }

        MatchedAnime = anime;
        OnPropertyChanged(nameof(CurrentMedia));
        if (anime != null)
        {
            IsManuallyMapped = _trackingService.IsManuallyMapped();
            LogDetection(CurrentMedia ?? new ParsedMedia { AnimeTitle = anime.Title }, UIUtils.GetLoc("scrobbler.status.matched"));

            // Force fetch + apply Russian metadata if enabled and missing.
            // EnsureLocalizedAsync handles the cache-miss → API fetch path
            // AND copies meta.Russian/Description into the AnimeItem; the
            // previous code only called RefreshMetadata() which raises
            // PropertyChanged but never wrote the fetched values, so the
            // UI stayed empty whenever the DB had no Shiki row yet.
            EnsureLocalizedSafeAsync(anime).SafeFireAndForget("NowPlaying.AnimeMatched");
        }
        else
        {
            IsManuallyMapped = false;
        }
    }

    private async Task EnsureLocalizedSafeAsync(AnimeItem anime)
    {
        try
        {
            await _shikiMetadataService.EnsureLocalizedAsync(anime, _disposeCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "NowPlaying: EnsureLocalizedAsync failed for {Id}", anime.Id);
        }
    }

    public void Receive(MediaChangedMessage message)
    {
        var media = message.Media;
        // Drop the manual-selection guard only when the media actually changes
        // (different file or playback stopped). ManualMapAsync re-runs
        // MatchMediaAsync with the *same* media to force a re-match, which
        // re-fires MediaChanged; clearing the guard there would let the
        // intermediate AnimeMatched(null) event clobber the user's choice.
        var prev = CurrentMedia;
        bool sameFile = media != null && prev != null
            && string.Equals(prev.OriginalTitle, media.OriginalTitle, StringComparison.Ordinal);
        if (!sameFile) Volatile.Write(ref _pendingManualMatchId, 0);

        CurrentMedia = media;
        IsMediaDetected = media != null;
        Suggestions.Clear();
        ShowSuggestions = false;
        SearchQuery = string.Empty;
        IsSearchPanelOpen = false;
        TrackingStatus = string.Empty;
        OnPropertyChanged(nameof(HasSuggestions));
        if (media != null)
        {
            IsPaused = !media.IsPlaying;
            LogDetection(media, UIUtils.GetLoc("scrobbler.status.detected"));
        }
        else
        {
            MatchedAnime = null;
            IsManuallyMapped = false;
            CountdownStatus = string.Empty;
        }
    }

    [RelayCommand]
    private async Task RemoveMapping()
    {
        await _trackingService.RemoveManualMappingAsync();
    }

    [RelayCommand]
    private async Task UnlinkMatch()
    {
        if (IsManuallyMapped)
        {
            // Remove persisted manual mapping; tracking service will re-match.
            await _trackingService.RemoveManualMappingAsync();
        }
        else
        {
            // Auto-match: persist a negative mapping so future sessions won't auto-match either.
            await _trackingService.AddNegativeMappingAsync();
            MatchedAnime = null;
            IsManuallyMapped = false;
            if (CurrentMedia != null) SearchQuery = CurrentMedia.AnimeTitle;
            await OpenSearchPanel();
        }
    }

    [RelayCommand]
    private void GoToSettings()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationPage.Settings));
    }

    [RelayCommand]
    private void ConfirmMatch()
    {
        if (PendingMatch == null) return;
        MatchedAnime = PendingMatch;
        PendingMatch = null;
    }

    [RelayCommand]
    private void RejectMatch()
    {
        PendingMatch = null;
    }

    [RelayCommand]
    private async Task CopyMalLink()
    {
        if (MatchedAnime == null) return;
        string url = $"{Constants.Api.Mal.WebsiteUrl}{MatchedAnime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private async Task CopyShikiLink()
    {
        if (MatchedAnime == null) return;
        string url = $"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{MatchedAnime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        if (MatchedAnime == null) return;
        ShellLauncher.OpenUrl($"{Constants.Api.Mal.WebsiteUrl}{MatchedAnime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        if (MatchedAnime == null) return;
        ShellLauncher.OpenUrl($"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{MatchedAnime.Id}");
    }

    private static async Task CopyToClipboard(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
    }

    private void LogDetection(ParsedMedia media, string status)
    {
        string extras = string.Join(" ",
            new[] { media.VideoResolution, media.Source, media.AnimeType }
            .Where(s => !string.IsNullOrEmpty(s)));
        string extraInfo = !string.IsNullOrEmpty(extras) ? $" [{extras}]" : "";
        string epInfo = !string.IsNullOrEmpty(media.Episode) ? $" ({UIUtils.GetLoc("anime.labels.episode")} {media.Episode})" : "";
        string logEntry = $"[{DateTime.Now:HH:mm:ss}] {status}: {media.AnimeTitle}{epInfo}{extraInfo}";
        DetectionLogs.Insert(0, logEntry);
        if (DetectionLogs.Count > 50) DetectionLogs.RemoveAt(50);
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        try { _searchCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling search CTS during dispose"); }
        try { _searchCts?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing search CTS"); }
        try { _disposeCts.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling dispose CTS"); }
        try { _disposeCts.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing dispose CTS"); }
    }
}
