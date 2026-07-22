using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Tracking;

public partial class TrackingService
{
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

                _uiDispatcher.Post(() => CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Models.Messages.MediaChangedMessage(media)));
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
                CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Models.Messages.MediaChangedMessage(media));
                CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Models.Messages.AnimeMatchedMessage(null));
            });

            await Task.WhenAny(_animeRepo.InitializationTask, Task.Delay(5000));

            var userList = await _uiDispatcher.InvokeAsync(() => System.Linq.Enumerable.ToList(_animeRepo.Collection));
            var matched = System.Linq.Enumerable.FirstOrDefault(userList, x => x.Id == animeId);

            if (matched == null)
            {
                var activeTracker = System.Linq.Enumerable.FirstOrDefault(_trackers, t => t.IsEnabled);
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
                        Serilog.Log.Warning(ex, "Failed to fetch anime details for ID {AnimeId}", animeId);
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
                    _uiDispatcher.Post(() => CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new Kiriha.Models.Messages.AnimeMatchedMessage(matched)));

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
            Serilog.Log.Error(ex, "Error during tracking mapping for internal player");
        }
    }

    private static bool IsSameMedia(ParsedMedia? a, ParsedMedia? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        return a.AnimeTitle == b.AnimeTitle && a.Episode == b.Episode;
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
