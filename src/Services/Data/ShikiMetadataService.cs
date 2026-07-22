using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Serilog;

namespace Kiriha.Services.Data;

public partial class ShikiMetadataService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Repositories.IMetadataRepository _metadataRepo;
    private readonly Repositories.IUserAnimeRepository _userAnimeRepo;
    private readonly SettingsService _settingsService;
    private readonly HttpConditionalCache _httpCache;
    private readonly ShikiHostResolver _hostResolver;
    private readonly IUiDispatcher _uiDispatcher;

    private readonly SemaphoreSlim _rateLimitLock = new(1, 1);
    private readonly SemaphoreSlim _concurrentFetches = new(2, 2);
    private readonly ConcurrentDictionary<int, byte> _activeFetches = new();
    private DateTime _lastRequest = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(250);

    public ShikiMetadataService(
        IHttpClientFactory httpClientFactory,
        SettingsService settingsService,
        Repositories.IMetadataRepository metadataRepo,
        Repositories.IUserAnimeRepository userAnimeRepo,
        Repositories.IHttpCacheRepository httpCacheRepo,
        ShikiHostResolver hostResolver,
        IUiDispatcher uiDispatcher)
    {
        _httpClient = httpClientFactory.CreateClient("ShikiClient");
        _settingsService = settingsService;
        _metadataRepo = metadataRepo;
        _userAnimeRepo = userAnimeRepo;
        _hostResolver = hostResolver;
        _uiDispatcher = uiDispatcher;
        _httpCache = new HttpConditionalCache(
            _httpClient,
            httpCacheRepo,
            "ShikiMeta",
            (client, request, innerCt) => ShikiHttp.SendShikiAsync(client, request, _hostResolver, innerCt));
    }

    // Resolved per-call so a mid-session mirror switch is honoured immediately.
    // "ShikiClient" has no HttpClient.BaseAddress, so we must always send absolute URLs.
    private string ShikiBaseUrl => ShikiEndpoints.BaseUrl(_settingsService.Current.Api.ShikiMirror);

    private static int GetCacheId(int malId, MediaKind mediaKind) =>
        mediaKind switch
        {
            MediaKind.Manga => malId | 0x40000000,
            MediaKind.LightNovel => malId | 0x20000000,
            _ => malId
        };

    /// <summary>
    /// Returns Shikimori metadata for <paramref name="animeId"/>, fetching from
    /// the API on miss. <paramref name="maxAge"/> bounds the cache freshness:
    /// when the persisted entry is older, we re-fetch (the conditional GET via
    /// <see cref="HttpConditionalCache"/> makes this cheap — usually 304).
    /// Pass <c>null</c> to accept any age (default for completed shows).
    ///
    /// <paramref name="onFetched"/> is invoked on every successful return —
    /// cache hit or fresh fetch — so periodic syncs (e.g. AiringInfoService's
    /// Shiki fallback) keep applying current values to the UI.
    /// </summary>
    public async Task<ShikiMetadata?> GetOrFetchMetadataAsync(int animeId, TimeSpan? maxAge = null, Func<ShikiMetadata, Task>? onFetched = null, MediaKind mediaKind = MediaKind.Anime)
    {
        int cacheId = GetCacheId(animeId, mediaKind);
        var cached = await _metadataRepo.GetAsync(cacheId);
        // When a TTL is requested, treat both genuinely-old entries and pre-TTL
        // legacy rows (FetchedAt == default after the schema migration) as stale —
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

        if (!_activeFetches.TryAdd(cacheId, 0))
        {
            // Another fetch is already in flight; serve whatever we have
            // (possibly stale) rather than spinning a duplicate request.
            return cached;
        }

        try
        {
            var fetched = await FetchMetadataFromApiAsync(animeId, CancellationToken.None, mediaKind);
            if (fetched != null)
            {
                await _metadataRepo.UpsertAsync(fetched);
                if (onFetched != null) await onFetched(fetched);
                return fetched;
            }

            // Live fetch failed but we still have a stale entry — better to
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
            _activeFetches.TryRemove(cacheId, out _);
        }
    }

    /// <summary>
    /// Backwards-compatible overload: caller doesn't need a TTL window.
    /// </summary>
    public Task<ShikiMetadata?> GetOrFetchMetadataAsync(int animeId, Func<ShikiMetadata, Task>? onFetched, MediaKind mediaKind = MediaKind.Anime)
        => GetOrFetchMetadataAsync(animeId, maxAge: null, onFetched, mediaKind);

    private async Task<ShikiMetadata?> FetchMetadataFromApiAsync(int animeId, CancellationToken ct = default, MediaKind mediaKind = MediaKind.Anime)
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
                    string endpoint = mediaKind == MediaKind.Anime ? "animes" : "mangas";
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{ShikiBaseUrl}{endpoint}/{animeId}");
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
                return new ShikiMetadata { Id = GetCacheId(animeId, mediaKind), Russian = "", Description = "" };
            }

            if (result.Body == null) return null; // transient failure — retry next tick

            var metadata = System.Text.Json.JsonSerializer.Deserialize<ShikiMetadata>(result.Body);
            if (metadata != null)
                metadata.Id = GetCacheId(animeId, mediaKind);
            return metadata;
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
    /// (≈4 RPS) to leave headroom for transient bursts.
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



    public void Dispose()
    {
        _rateLimitLock.Dispose();
        _concurrentFetches.Dispose();
    }
}
