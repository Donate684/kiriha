using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.Services.Data;

public partial class ShikiMetadataService
{
    public async Task LocalizeItemsAsync(IEnumerable<AnimeItem> items, Action<int>? onProgress = null, CancellationToken ct = default)
    {
        bool useRussian = _settingsService.Current.UI.UseRussianTitles || _settingsService.Current.UI.UseRussianDescriptions;
        bool showAiring = _settingsService.Current.UI.ShowAiringInfo;

        if (!useRussian && !showAiring)
            return;

        int localizedCount = 0;
        var uncached = new List<AnimeItem>();

        foreach (var item in items)
        {
            if (!useRussian && showAiring && item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.PlanToWatch)
                continue;

            int cacheId = GetCacheId(item.Id, item.MediaKind);
            var meta = await _metadataRepo.GetAsync(cacheId);
            if (meta != null)
            {
                // ApplyMetadata mutates ObservableObject properties — dispatch to UI thread.
                bool changed = await _uiDispatcher.InvokeAsync(() => ApplyMetadata(item, meta));
                if (changed) await _userAnimeRepo.UpdateMetadataAsync(item);
                localizedCount++;
            }
            else
            {
                uncached.Add(item);
            }
        }

        onProgress?.Invoke(localizedCount);

        if (uncached.Count > 0)
        {
            var tasks = new List<Task>();
            foreach (var item in uncached)
            {
                int cacheId = GetCacheId(item.Id, item.MediaKind);
                if (!_activeFetches.TryAdd(cacheId, 0)) continue;

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await _concurrentFetches.WaitAsync(ct);
                        var fetchedMeta = await FetchMetadataFromApiAsync(item.Id, ct, item.MediaKind);
                        if (fetchedMeta != null)
                        {
                            await _metadataRepo.UpsertAsync(fetchedMeta);

                            bool changed = await _uiDispatcher.InvokeAsync(() => ApplyMetadata(item, fetchedMeta));
                            if (changed) await _userAnimeRepo.UpdateMetadataAsync(item);
                            int count = System.Threading.Interlocked.Increment(ref localizedCount);
                            onProgress?.Invoke(count);
                        }
                    }
                    finally
                    {
                        _concurrentFetches.Release();
                        _activeFetches.TryRemove(cacheId, out _);
                    }
                }, ct));
            }
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Ensure <paramref name="item"/>'s Russian title/synopsis are populated
    /// from the Shikimori metadata cache, fetching from the API on miss.
    /// Mirrors the per-item path inside <see cref="LocalizeItemsAsync"/> so
    /// callers (e.g. NowPlaying) get the same write-through-and-persist
    /// behaviour without rolling their own apply logic — bare
    /// <c>AnimeItem.RefreshMetadata()</c> only raises PropertyChanged, it
    /// does NOT copy fetched fields into the item.
    /// </summary>
    public async Task EnsureLocalizedAsync(AnimeItem item, CancellationToken ct = default)
    {
        if (item == null) return;
        bool useRussian = _settingsService.Current.UI.UseRussianTitles || _settingsService.Current.UI.UseRussianDescriptions;
        if (!useRussian) return;
        if (!string.IsNullOrEmpty(item.RussianTitle) && !string.IsNullOrEmpty(item.RussianSynopsis)) return;

        bool missingData = (string.IsNullOrEmpty(item.RussianTitle) && _settingsService.Current.UI.UseRussianTitles) ||
                           (string.IsNullOrEmpty(item.RussianSynopsis) && _settingsService.Current.UI.UseRussianDescriptions);
        TimeSpan? maxAge = missingData ? TimeSpan.FromHours(12) : null;

        var meta = await GetOrFetchMetadataAsync(item.Id, maxAge: maxAge, onFetched: null, item.MediaKind);
        if (meta == null) return;

        bool changed = await _uiDispatcher.InvokeAsync(() => ApplyMetadata(item, meta));
        if (changed)
        {
            try { await _userAnimeRepo.UpdateMetadataAsync(item); }
            catch (Exception ex) { Log.Debug(ex, "EnsureLocalizedAsync: failed to persist localized fields for {Id}", item.Id); }
        }
    }

    private bool ApplyMetadata(AnimeItem item, ShikiMetadata meta)
    {
        bool changed = false;
        if (_settingsService.Current.UI.UseRussianTitles && !string.IsNullOrEmpty(meta.Russian))
        {
            if (item.RussianTitle != meta.Russian) { item.RussianTitle = meta.Russian; changed = true; }
        }

        if (_settingsService.Current.UI.UseRussianDescriptions && !string.IsNullOrEmpty(meta.Description))
        {
            var cleaned = Kiriha.Utils.Parsing.AnimeStringHelper.CleanShikiDescription(meta.Description);
            if (item.RussianSynopsis != cleaned) { item.RussianSynopsis = cleaned; changed = true; }
        }

        // EpisodesAired and NextEpisodeAt are intentionally NOT applied from Shikimori.
        // Shiki's `episodes_aired` is known to lead `next_episode_at` by one (e.g. claims
        // 5 aired while next_episode_at still points at episode 5 in the future), which
        // used to inflate our counter. Airing state is owned by AniList's
        // nextAiringEpisode in AiringInfoService.

        if (changed) item.RefreshMetadata();
        return changed;
    }

    public async Task<string?> ResolveRussianQueryAsync(string query, CancellationToken ct = default)
    {
        try
        {
            var bytes = await _httpCache.SendAsync(
                requestFactory: innerCt =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{ShikiBaseUrl}animes?search={Uri.EscapeDataString(query)}&limit=1");
                    request.Headers.Add("User-Agent", Core.AppInfo.UserAgent);
                    return Task.FromResult(request);
                },
                throttle: ShikiThrottleAsync,
                ct: ct);

            if (bytes == null) return null;

            var list = System.Text.Json.JsonSerializer.Deserialize<List<ShikiMetadata>>(bytes);
            if (list != null && list.Count > 0)
                return list[0].Name;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to resolve Russian query on Shikimori");
        }
        return null;
    }
}
