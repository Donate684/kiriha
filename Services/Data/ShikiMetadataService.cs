using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Serilog;

namespace Kiriha.Services.Data;

public class ShikiMetadataService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Repositories.IMetadataRepository _metadataRepo;
    private readonly Repositories.IUserAnimeRepository _userAnimeRepo;
    private readonly SettingsService _settingsService;
    private readonly HttpConditionalCache _httpCache;
    private readonly ShikiHostResolver _hostResolver;

    private readonly SemaphoreSlim _rateLimitLock = new(1, 1);
    private readonly SemaphoreSlim _concurrentFetches = new(2, 2);
    private readonly HashSet<int> _activeFetches = new();
    private DateTime _lastRequest = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(250); 

    public ShikiMetadataService(
        IHttpClientFactory httpClientFactory,
        SettingsService settingsService,
        Repositories.IMetadataRepository metadataRepo,
        Repositories.IUserAnimeRepository userAnimeRepo,
        Repositories.IHttpCacheRepository httpCacheRepo,
        ShikiHostResolver hostResolver)
    {
        _httpClient = httpClientFactory.CreateClient("ShikiClient");
        _settingsService = settingsService;
        _metadataRepo = metadataRepo;
        _userAnimeRepo = userAnimeRepo;
        _hostResolver = hostResolver;
        _httpCache = new HttpConditionalCache(
            _httpClient,
            httpCacheRepo,
            "ShikiMeta",
            (client, request, innerCt) => ShikiHttp.SendShikiAsync(client, request, _hostResolver, innerCt));
    }

    // Resolved per-call so a mid-session mirror switch is honoured immediately.
    // "ShikiClient" has no HttpClient.BaseAddress, so we must always send absolute URLs.
    private string ShikiBaseUrl => ShikiEndpoints.BaseUrl(_settingsService.Current.Api.ShikiMirror);

    /// <summary>
    /// Returns Shikimori metadata for <paramref name="animeId"/>, fetching from
    /// the API on miss. <paramref name="maxAge"/> bounds the cache freshness:
    /// when the persisted entry is older, we re-fetch (the conditional GET via
    /// <see cref="HttpConditionalCache"/> makes this cheap Ã¢â‚¬â€ usually 304).
    /// Pass <c>null</c> to accept any age (default for completed shows).
    ///
    /// <paramref name="onFetched"/> is invoked on every successful return Ã¢â‚¬â€
    /// cache hit or fresh fetch Ã¢â‚¬â€ so periodic syncs (e.g. AiringInfoService's
    /// Shiki fallback) keep applying current values to the UI.
    /// </summary>
    public async Task<ShikiMetadata?> GetOrFetchMetadataAsync(int animeId, TimeSpan? maxAge = null, Func<ShikiMetadata, Task>? onFetched = null)
    {
        var cached = await _metadataRepo.GetAsync(animeId);
        // When a TTL is requested, treat both genuinely-old entries and pre-TTL
        // legacy rows (FetchedAt == default after the schema migration) as stale Ã¢â‚¬â€
        // otherwise a user's existing metadata would skip the airing refresh
        // forever. The first successful upsert stamps a real timestamp and
        // normal TTL semantics take over.
        bool stale = cached != null
                     && maxAge.HasValue
                     && (cached.FetchedAt == default
                         || DateTime.UtcNow - cached.FetchedAt > maxAge.Value);

        if (cached != null && !stale)
        {
            if (onFetched != null) await onFetched(cached);
            return cached;
        }

        lock (_activeFetches)
        {
            if (_activeFetches.Contains(animeId))
            {
                // Another fetch is already in flight; serve whatever we have
                // (possibly stale) rather than spinning a duplicate request.
                return cached;
            }
            _activeFetches.Add(animeId);
        }

        try
        {
            var fetched = await FetchMetadataFromApiAsync(animeId, CancellationToken.None);
            if (fetched != null)
            {
                await _metadataRepo.UpsertAsync(fetched);
                if (onFetched != null) await onFetched(fetched);
                return fetched;
            }

            // Live fetch failed but we still have a stale entry Ã¢â‚¬â€ better to
            // return it than nothing, the caller can apply best-effort.
            if (cached != null)
            {
                if (onFetched != null) await onFetched(cached);
                return cached;
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Background check for metadata failed for {Id}", animeId);
            return cached;
        }
        finally
        {
            lock (_activeFetches) { _activeFetches.Remove(animeId); }
        }
    }

    /// <summary>
    /// Backwards-compatible overload: caller doesn't need a TTL window.
    /// </summary>
    public Task<ShikiMetadata?> GetOrFetchMetadataAsync(int animeId, Func<ShikiMetadata, Task>? onFetched)
        => GetOrFetchMetadataAsync(animeId, maxAge: null, onFetched);

    private async Task<ShikiMetadata?> FetchMetadataFromApiAsync(int animeId, CancellationToken ct = default)
    {
        // Conditional GET via http_response_cache: on a 304 we skip JSON parse
        // entirely (the helper replays the persisted body, which we then parse).
        // ResilientHttpHandler still handles 429s with backoff, so the manual
        // retry loop the original code carried is no longer needed.
        try
        {
            var result = await _httpCache.SendForResultAsync(
                requestFactory: innerCt =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{ShikiBaseUrl}animes/{animeId}");
                    request.Headers.Add("User-Agent", AppInfo.UserAgent);
                    return Task.FromResult(request);
                },
                throttle: ShikiThrottleAsync,
                ct: ct);

            // 404: anime not on Shikimori. Persist a sentinel so we don't
            // re-attempt on every sync tick (matches the original behaviour
            // of the manual retry loop).
            if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ShikiMetadata { Id = animeId, Russian = "", Description = "" };
            }

            if (result.Body == null) return null; // transient failure Ã¢â‚¬â€ retry next tick

            return System.Text.Json.JsonSerializer.Deserialize<ShikiMetadata>(result.Body);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception fetching Shikimori metadata for {Id}", animeId);
            return null;
        }
    }

    /// <summary>
    /// Shikimori rate limit: 5 RPS. We pace at 250 ms between requests
    /// (Ã¢â€°Ë†4 RPS) to leave headroom for transient bursts.
    /// </summary>
    private async Task ShikiThrottleAsync(CancellationToken ct)
    {
        await _rateLimitLock.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequest;
            if (elapsed < _minInterval)
                await Task.Delay(_minInterval - elapsed, ct);
            _lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _rateLimitLock.Release();
        }
    }

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

            var meta = await _metadataRepo.GetAsync(item.Id);
            if (meta != null)
            {
                // ApplyMetadata mutates ObservableObject properties Ã¢â‚¬â€ dispatch to UI thread.
                bool changed = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplyMetadata(item, meta));
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
                lock (_activeFetches) {
                    if (_activeFetches.Contains(item.Id)) continue;
                    _activeFetches.Add(item.Id);
                }

                tasks.Add(Task.Run(async () => 
                {
                    try {
                        await _concurrentFetches.WaitAsync(ct);
                        var fetchedMeta = await FetchMetadataFromApiAsync(item.Id, ct);
                        if (fetchedMeta != null)
                        {
                            await _metadataRepo.UpsertAsync(fetchedMeta);
                            
                            Avalonia.Threading.Dispatcher.UIThread.Post(async () => {
                                bool changed = ApplyMetadata(item, fetchedMeta);
                                if (changed) await _userAnimeRepo.UpdateMetadataAsync(item);
                                int count = Interlocked.Increment(ref localizedCount);
                                onProgress?.Invoke(count);
                            });
                        }
                    }
                    finally {
                        _concurrentFetches.Release();
                        lock (_activeFetches) { _activeFetches.Remove(item.Id); }
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
    /// behaviour without rolling their own apply logic Ã¢â‚¬â€ bare
    /// <c>AnimeItem.RefreshMetadata()</c> only raises PropertyChanged, it
    /// does NOT copy fetched fields into the item.
    /// </summary>
    public async Task EnsureLocalizedAsync(AnimeItem item, CancellationToken ct = default)
    {
        if (item == null) return;
        bool useRussian = _settingsService.Current.UI.UseRussianTitles || _settingsService.Current.UI.UseRussianDescriptions;
        if (!useRussian) return;
        if (!string.IsNullOrEmpty(item.RussianTitle) && !string.IsNullOrEmpty(item.RussianSynopsis)) return;

        var meta = await GetOrFetchMetadataAsync(item.Id, maxAge: null, onFetched: null);
        if (meta == null) return;

        bool changed = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplyMetadata(item, meta));
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
            var cleaned = Kiriha.Utils.AnimeStringHelper.CleanShikiDescription(meta.Description);
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
                    request.Headers.Add("User-Agent", AppInfo.UserAgent);
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

    public void Dispose()
    {
        _rateLimitLock.Dispose();
        _concurrentFetches.Dispose();
    }
}
