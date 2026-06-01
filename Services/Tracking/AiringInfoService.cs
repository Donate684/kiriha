using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.Services.Tracking;

public class AiringInfoService
{
    private readonly AniListApiService _aniListApi;
    private readonly AnimeService _animeService;
    private readonly NotificationService _notificationService;
    private readonly IUiDispatcher _uiDispatcher;

    public AiringInfoService(
        AniListApiService aniListApi,
        AnimeService animeService,
        NotificationService notificationService,
        IUiDispatcher uiDispatcher)
    {
        _aniListApi = aniListApi;
        _animeService = animeService;
        _notificationService = notificationService;
        _uiDispatcher = uiDispatcher;
    }

    /// <summary>
    /// Resolve airing state from AniList's nextAiringEpisode. AniList exposes a
    /// concrete future episode number and Unix timestamp, so there is no local
    /// prediction math: if episode 8 is next, at least 7 episodes have aired.
    /// </summary>
    private static (int aired, DateTime nextSlot) ResolveAired(AnimeItem anime, AniListAiringInfo airing)
    {
        var aired = Math.Max(0, airing.NextEpisode - 1);
        if (anime.TotalEpisodes > 0 && aired > anime.TotalEpisodes)
            aired = anime.TotalEpisodes;
        return (aired, airing.NextEpisodeAt);
    }

    public async Task SyncEpisodesForAnimeAsync(AnimeItem anime, CancellationToken ct = default)
    {
        if (_animeService.IsSyncing) return;
        if (anime.StatusDetailed != "currently_airing" || anime.Status != UserAnimeStatus.Watching) return;

        Log.Information("AiringInfoService: Immediate AniList sync requested for {Title} (ID: {Id})", anime.Title, anime.Id);

        var airing = await _aniListApi.GetNextAiringAsync(anime.Id, force: true, ct);
        if (_animeService.IsRecentlyDeleted(anime.Id)) return;

        if (airing == null)
        {
            await MarkSyncedAsync(anime, DateTime.Now);
            await _animeService.AddOrUpdateAnimeAsync(anime);
            return;
        }

        await ApplyAiringAsync(anime, airing, DateTime.Now);
        await _animeService.AddOrUpdateAnimeAsync(anime);
    }

    public async Task SyncOngoingEpisodesAsync(bool force = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (_animeService.IsSyncing)
        {
            Log.Information("AiringInfoService: Main sync is in progress, skipping episode sync to avoid DB conflicts.");
            return;
        }

        Log.Information("AiringInfoService: Checking AniList airing info (Force: {Force})...", force);

        var threshold = DateTime.Now.AddHours(-6);
        // Snapshot on UI thread - ObservableCollection is not thread-safe.
        var toSync = await _uiDispatcher.InvokeAsync(() =>
            _animeService.Collection
                .Where(x => x.StatusDetailed == "currently_airing" &&
                            x.Status == UserAnimeStatus.Watching &&
                            (force || x.LastEpisodesSync == null || x.LastEpisodesSync < threshold))
                .ToList());

        if (!toSync.Any())
        {
            Log.Information("AiringInfoService: No anime needs syncing at this time.");
            return;
        }

        Log.Information("AiringInfoService: Found {Count} anime to sync from AniList.", toSync.Count);

        for (int i = 0; i < toSync.Count; i++)
        {
            var anime = toSync[i];
            if (ct.IsCancellationRequested) break;
            if (_animeService.IsRecentlyDeleted(anime.Id)) continue;

            var progressMsg = UIUtils.GetLoc("sync.syncing.episodes_progress", (i + 1).ToString(), toSync.Count.ToString(), anime.Title);
            progress?.Report(progressMsg);

            Log.Information("AiringInfoService: Syncing AniList airing info for {Title} (ID: {Id})...", anime.Title, anime.Id);

            var now = DateTime.Now;
            var airing = await _aniListApi.GetNextAiringAsync(anime.Id, force, ct);
            if (airing == null)
            {
                await MarkSyncedAsync(anime, now);
                await _animeService.AddOrUpdateAnimeAsync(anime);
                continue;
            }

            await ApplyAiringAsync(anime, airing, now);
            await _animeService.AddOrUpdateAnimeAsync(anime);
        }

        Log.Information("AiringInfoService: AniList sync cycle completed.");
    }

    private async Task ApplyAiringAsync(AnimeItem anime, AniListAiringInfo airing, DateTime now)
    {
        var (finalAiredCount, nextSlot) = ResolveAired(anime, airing);
        int? notifyEp = null;

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (finalAiredCount != anime.EpisodesAired)
            {
                if (anime.EpisodesAired > 0 && finalAiredCount > anime.EpisodesAired)
                {
                    anime.LastEpisodeAt = now;
                    notifyEp = finalAiredCount;
                }

                anime.EpisodesAired = finalAiredCount;
                anime.AiredSourcePriority = 4;
            }

            anime.NextEpisodeAt = nextSlot;
            anime.LastEpisodesSync = now;
            anime.RefreshMetadata();
        });

        if (notifyEp.HasValue)
        {
            Log.Information("AiringInfoService: New episode detected for {Title}: {Count}", anime.Title, notifyEp.Value);
            _notificationService.NotifyNewEpisode(anime, notifyEp.Value);
        }
    }

    private async Task MarkSyncedAsync(AnimeItem anime, DateTime now)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            anime.LastEpisodesSync = now;
            anime.RefreshMetadata();
        });
    }
}
