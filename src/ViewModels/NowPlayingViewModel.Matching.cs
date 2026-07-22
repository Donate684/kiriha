using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Models.Messages;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.ViewModels;

public partial class NowPlayingViewModel
{
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
}
