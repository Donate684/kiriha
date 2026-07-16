using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Api;

public class MalApiService : ITrackerService, IDisposable
{
    // Resolved once from Constants — no parallel const that can drift from the URL
    // wired into the IHttpClientFactory "MalClient" registration.
    private static readonly string MalBaseUrl = Constants.Api.Mal.BaseUrl;
    
    private static readonly string ListStatusFields = "num_episodes_watched,score,status,num_times_rewatched,is_rewatching,notes,start_date,finish_date";
    private static readonly string AnimeFields = $"list_status{{{ListStatusFields}}},my_list_status{{{ListStatusFields}}},main_picture,synopsis,mean,rank,popularity,num_episodes,start_season,genres,studios,alternative_titles,status,start_date,nsfw,rating,media_type,broadcast,external_links";

    private static readonly string MangaListStatusFields = "num_chapters_read,num_volumes_read,score,status,num_times_reread,is_rereading,notes,start_date,finish_date";
    private static readonly string MangaFields = $"list_status{{{MangaListStatusFields}}},my_list_status{{{MangaListStatusFields}}},main_picture,synopsis,mean,rank,popularity,num_chapters,num_volumes,authors,genres,alternative_titles,status,start_date,nsfw,media_type,external_links";

    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly JikanApiService _jikanApi;
    private readonly HttpConditionalCache _httpCache;
    private readonly SemaphoreSlim _tokenRefreshLock = new(1, 1);
    // Outbound throttle: ~3.3 req/s (one token every 300 ms). MAL doesn't publish a
    // hard rate-limit but Cloudflare in front of api.myanimelist.net bites at ~5 req/s
    // sustained; 300 ms keeps us comfortably below that and avoids 429 storms when
    // SyncManager flushes a backlog.
    private readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 100,
        ReplenishmentPeriod = TimeSpan.FromMilliseconds(300),
        TokensPerPeriod = 1,
        AutoReplenishment = true,
    });

    // Consecutive refresh failures. After hitting the threshold we clear the saved token so
    // SyncManager stops generating doomed retry tasks against a revoked refresh token.
    private int _refreshFailures;
    private const int MaxRefreshFailures = 3;

    public string Name => "MyAnimeList";
    public bool IsEnabled => _settingsService.Current.Api.Mal != null;

    public MalApiService(HttpClient httpClient, SettingsService settingsService, MalAuthService authService, JikanApiService jikanApi, IHttpCacheRepository httpCacheRepo)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _authService = authService;
        _jikanApi = jikanApi;
        _httpCache = new HttpConditionalCache(httpClient, httpCacheRepo, "MalApi");
    }

    public async Task<List<AnimeItem>?> GetUserAnimeListAsync(CancellationToken ct = default)
    {
        Log.Information("Syncing user list from MyAnimeList...");
        var list = new List<AnimeItem>();
        string? nextUrl = $"users/@me/animelist?limit=1000&fields={AnimeFields}&nsfw=true";
        int pageIndex = 0;

        try
        {
            while (!string.IsNullOrEmpty(nextUrl))
            {
                using var response = await GetAsync(nextUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // CRITICAL: Don't return a partial list. For users with >1000 anime MAL paginates,
                    // and a mid-pagination failure used to silently truncate the result. Sync then
                    // diff-removed the "missing" half from the local DB (visible as the watching tab
                    // suddenly having ~half the titles). Returning null lets SyncWithTrackersAsync
                    // bail out instead of corrupting the cached library.
                    Log.Warning("MAL list fetch: page {Page} returned {Status}; aborting sync to avoid partial-list overwrite.", pageIndex, response.StatusCode);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var json = await JsonDocument.ParseAsync(stream, default, ct);
                
                var root = json.RootElement;
                if (root.TryGetProperty("data", out var data))
                {
                    foreach (var entry in data.EnumerateArray())
                    {
                        var anime = MalMapper.MapJsonToAnimeItem(entry.GetProperty("node"));
                        if (entry.TryGetProperty("list_status", out var status)) MalMapper.MapListStatus(status, anime);
                        list.Add(anime);
                    }
                }
                nextUrl = root.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next) ? next.GetString() : null;
                pageIndex++;
            }
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Error fetching MAL list (after page {Page}); returning null to skip destructive sync.", pageIndex);
            return null;
        }
    }

    public async Task<List<AnimeItem>> GetSeasonalAnimeAsync(int year, string season, CancellationToken ct = default)
    {
        var list = new List<AnimeItem>();
        string? nextUrl = $"anime/season/{year}/{season}?limit=100&fields={AnimeFields}&nsfw=true";

        try
        {
            while (!string.IsNullOrEmpty(nextUrl))
            {
                var bytes = await GetWithCacheAsync(nextUrl, ct);
                if (bytes == null) break;

                using var json = JsonDocument.Parse(bytes);

                var root = json.RootElement;
                if (root.TryGetProperty("data", out var data))
                {
                    foreach (var node in data.EnumerateArray())
                    {
                        list.Add(MalMapper.MapJsonToAnimeItem(node.GetProperty("node")));
                    }
                }
                nextUrl = root.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next) ? next.GetString() : null;
            }
            return list;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: GetSeasonalAnimeAsync failed"); return list; }
    }

    public async Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_watched_episodes", episodes.ToString()) };
        if (score.HasValue) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None) values.Add(new("status", StatusMapper.ToMal(status.Value)));
        if (isRewatching.HasValue) values.Add(new("is_rewatching", isRewatching.Value.ToString().ToLower()));
        if (rewatchCount.HasValue) values.Add(new("num_times_rewatched", rewatchCount.Value.ToString()));

        return await SendPatchAsync($"anime/{animeId}/my_list_status", values, ct);
    }

    public async Task<SyncOutcome> SaveFullListStatusAsync(AnimeItem item, CancellationToken ct = default)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;
        
        var values = new List<KeyValuePair<string, string>>
        {
            new(isManga ? "num_chapters_read" : "num_watched_episodes", isManga ? item.ChaptersRead.ToString() : item.Progress.ToString()),
            new("status", StatusMapper.ToMal(item.Status, isManga)),
            new("num_times_rewatched", item.RewatchCount.ToString()),
            new("is_rewatching", item.IsRewatching.ToString().ToLower())
        };
        
        if (isManga)
        {
            values.Add(new("num_volumes_read", item.VolumesRead.ToString()));
        }
        
        if (int.TryParse(item.Score, out int s)) values.Add(new("score", s.ToString()));
        if (!string.IsNullOrEmpty(item.Notes)) values.Add(new("notes", item.Notes));
        if (item.DateStarted.HasValue) values.Add(new("start_date", item.DateStarted.Value.ToString("yyyy-MM-dd")));
        if (item.DateCompleted.HasValue) values.Add(new("finish_date", item.DateCompleted.Value.ToString("yyyy-MM-dd")));

        var endpoint = isManga ? $"manga/{item.Id}/my_list_status" : $"anime/{item.Id}/my_list_status";
        return await SendPatchAsync(endpoint, values, ct);
    }

    public async Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default)
    {
        // MAL returns 404 if the anime is already not on the list — treat as Success so
        // a redundant Remove doesn't get queued forever after the user deleted it via web.
        var outcome = await SendRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, MalBaseUrl + $"anime/{animeId}/my_list_status"),
            ct);
        return outcome == SyncOutcome.PermanentFailure ? SyncOutcome.Success : outcome;
    }

    public async Task<List<AnimeItem>> SearchAnimeAsync(string query, CancellationToken ct = default)
    {
        var list = new List<AnimeItem>();
        var url = $"anime?q={Uri.EscapeDataString(query)}&limit=50&fields={AnimeFields}&nsfw=true";
        try
        {
            var bytes = await GetWithCacheAsync(url, ct);
            if (bytes == null) return list;

            using var json = JsonDocument.Parse(bytes);

            if (json.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var node in data.EnumerateArray()) list.Add(MalMapper.MapJsonToAnimeItem(node.GetProperty("node")));
            }
            return list;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: SearchAnimeAsync failed"); return list; }
    }

    public async Task<AnimeItem?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default)
    {
        try
        {
            var bytes = await GetWithCacheAsync($"anime/{animeId}?fields={AnimeFields}&nsfw=true", ct);
            if (bytes == null) return null;
            
            using var json = JsonDocument.Parse(bytes);
            return MalMapper.MapJsonToAnimeItem(json.RootElement);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: GetAnimeDetailsAsync failed for {Id}", animeId); return null; }
    }

    public async Task<List<AnimeItem>?> GetUserMangaListAsync(CancellationToken ct = default)
    {
        Log.Information("Syncing user manga list from MyAnimeList...");
        var list = new List<AnimeItem>();
        string? nextUrl = $"users/@me/mangalist?limit=1000&fields={MangaFields}&nsfw=true";
        int pageIndex = 0;

        try
        {
            while (!string.IsNullOrEmpty(nextUrl))
            {
                using var response = await GetAsync(nextUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("MAL manga list fetch: page {Page} returned {Status}; aborting sync to avoid partial-list overwrite.", pageIndex, response.StatusCode);
                    return null;
                }

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var json = await JsonDocument.ParseAsync(stream, default, ct);
                
                var root = json.RootElement;
                if (root.TryGetProperty("data", out var data))
                {
                    foreach (var entry in data.EnumerateArray())
                    {
                        var anime = MalMapper.MapJsonToAnimeItem(entry.GetProperty("node"));
                        if (entry.TryGetProperty("list_status", out var status)) MalMapper.MapListStatus(status, anime);
                        list.Add(anime);
                    }
                }
                nextUrl = root.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out var next) ? next.GetString() : null;
                pageIndex++;
            }
            return list;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Error fetching MAL manga list (after page {Page}); returning null to skip destructive sync.", pageIndex);
            return null;
        }
    }

    public async Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_chapters_read", chapters.ToString()) };
        if (volumes.HasValue) values.Add(new("num_volumes_read", volumes.Value.ToString()));
        if (score.HasValue) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None) values.Add(new("status", StatusMapper.ToMal(status.Value, true)));

        return await SendPatchAsync($"manga/{mangaId}/my_list_status", values, ct);
    }

    public async Task<List<AnimeItem>> SearchMangaAsync(string query, CancellationToken ct = default)
    {
        var list = new List<AnimeItem>();
        var url = $"manga?q={Uri.EscapeDataString(query)}&limit=50&fields={MangaFields}&nsfw=true";
        try
        {
            var bytes = await GetWithCacheAsync(url, ct);
            if (bytes == null) return list;

            using var json = JsonDocument.Parse(bytes);

            if (json.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var node in data.EnumerateArray()) list.Add(MalMapper.MapJsonToAnimeItem(node.GetProperty("node")));
            }
            return list;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: SearchMangaAsync failed"); return list; }
    }

    public async Task<AnimeItem?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default)
    {
        try
        {
            var bytes = await GetWithCacheAsync($"manga/{mangaId}?fields={MangaFields}&nsfw=true", ct);
            if (bytes == null) return null;
            
            using var json = JsonDocument.Parse(bytes);
            return MalMapper.MapJsonToAnimeItem(json.RootElement);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: GetMangaDetailsAsync failed for {Id}", mangaId); return null; }
    }

    public Task<List<EpisodeRelease>> GetEpisodeListAsync(int malId, CancellationToken ct = default) => 
        _jikanApi.GetEpisodeListAsync(malId, ct);

    public Task<int?> GetLatestEpisodeFromForumAsync(int malId, CancellationToken ct = default) => 
        _jikanApi.GetLatestEpisodeFromForumAsync(malId, ct);

    private Task<SyncOutcome> SendPatchAsync(string url, List<KeyValuePair<string, string>> values, CancellationToken ct)
    {
        // Factory — the value collection is captured once, but the HttpRequestMessage
        // is rebuilt per attempt so a 401-retry can re-issue the same logical request
        // without tripping HttpClient's "request already sent" guard.
        return SendRequestAsync(
            () => new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = new FormUrlEncodedContent(values) },
            ct);
    }

    /// <summary>
    /// Sends a request through the MAL pipeline with automatic 401→refresh→retry.
    /// </summary>
    /// <param name="requestFactory">
    /// Builds a fresh <see cref="HttpRequestMessage"/> per attempt. We need this
    /// because <see cref="HttpClient.SendAsync(HttpRequestMessage, CancellationToken)"/>
    /// disposes the request's content stream after sending, so a second send of
    /// the same instance would throw.
    /// </param>
    private async Task<SyncOutcome> SendRequestAsync(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        var token = await EnsureValidTokenAsync(ct);
        var statusCode = await SendOnceAsync(requestFactory, token, ct);
        if (statusCode == null) return SyncOutcome.TransientFailure;

        if (statusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Server-side token revocation, or the local IsExpired heuristic missed
            // a clock skew. Force a refresh and retry exactly once — looping further
            // would just hammer the auth endpoint with an invalid refresh token.
            Log.Information("MalApiService: 401 on first attempt; forcing token refresh and retrying once");
            token = await EnsureValidTokenAsync(ct, forceRefresh: true);
            if (string.IsNullOrEmpty(token))
            {
                Log.Warning("MalApiService: 401 retry aborted - token refresh failed");
                return SyncOutcome.PermanentFailure;
            }
            statusCode = await SendOnceAsync(requestFactory, token, ct);
            if (statusCode == null) return SyncOutcome.TransientFailure;
        }

        return MapStatusToOutcome(statusCode.Value);
    }

    private async Task<System.Net.HttpStatusCode?> SendOnceAsync(Func<HttpRequestMessage> requestFactory, string? token, CancellationToken ct)
    {
        var request = requestFactory();
        request.Headers.Add("User-Agent", AppInfo.UserAgent);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        try
        {
            await ThrottleAsync(ct);
            using var response = await _httpClient.SendAsync(request, ct);
            return response.StatusCode;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Network / DNS / TLS failures are transient — caller maps null → TransientFailure.
            Log.Warning(ex, "MalApiService: SendOnceAsync failed ({Method} {Uri})", request.Method, request.RequestUri);
            return null;
        }
        finally
        {
            request.Dispose();
        }
    }

    private static SyncOutcome MapStatusToOutcome(System.Net.HttpStatusCode status)
    {
        if ((int)status >= 200 && (int)status < 300) return SyncOutcome.Success;
        // 5xx + 408 + 429 are explicitly retriable. Note: ResilientHttpHandler already
        // burned through its retries for 5xx/429, so seeing one here means the server
        // is *still* misbehaving — still classify as Transient so SyncManager backs off
        // on a longer timescale (minutes) than the handler's seconds-scale retries.
        if ((int)status >= 500 || status == System.Net.HttpStatusCode.RequestTimeout || status == System.Net.HttpStatusCode.TooManyRequests)
        {
            Log.Warning("MalApiService: transient HTTP {Status}", (int)status);
            return SyncOutcome.TransientFailure;
        }
        Log.Warning("MalApiService: permanent HTTP {Status}", (int)status);
        return SyncOutcome.PermanentFailure;
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        // AcquireAsync queues the caller until a token is replenished; cancellation
        // is honoured by RateLimiter natively. The lease is disposed immediately
        // because we only use it as a wait primitive (no token return semantics).
        using var lease = await _rateLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new HttpRequestException("Rate limit queue exceeded.");
    }

    /// <summary>
    /// Returns a usable access token, refreshing if it is expired (or if
    /// <paramref name="forceRefresh"/> is set, e.g. after a 401 from the server).
    /// Coordinates concurrent refreshes through <see cref="_tokenRefreshLock"/> so
    /// a burst of API calls only triggers one refresh round-trip.
    /// </summary>
    private async Task<string?> EnsureValidTokenAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        var tokens = _settingsService.Current.Api.Mal;
        if (tokens == null) return null;
        if (!forceRefresh && !tokens.IsExpired) return tokens.AccessToken;

        await _tokenRefreshLock.WaitAsync(ct);
        try
        {
            tokens = _settingsService.Current.Api.Mal;
            if (tokens == null) return null;
            // Double-check: another caller may have already refreshed while we waited.
            if (!forceRefresh && !tokens.IsExpired) return tokens.AccessToken;

            var newTokens = await _authService.RefreshTokenAsync(tokens.RefreshToken, ct);
            if (newTokens != null)
            {
                _refreshFailures = 0;
                _settingsService.Update(settings => settings.Api.Mal = newTokens, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                return newTokens.AccessToken;
            }

            // Transient errors should not nuke the user's tokens — but a sustained streak
            // (revoked refresh, deleted MAL app, etc.) means the saved token is dead weight.
            _refreshFailures++;
            if (_refreshFailures >= MaxRefreshFailures)
            {
                Log.Warning("MalApiService: clearing saved tokens after {Count} consecutive refresh failures. User must re-authenticate.", _refreshFailures);
                _settingsService.Update(settings => settings.Api.Mal = null, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                _refreshFailures = 0;
            }
            return null;
        }
        finally { _tokenRefreshLock.Release(); }
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct = default)
    {
        var fullUrl = url.StartsWith("http") ? url : MalBaseUrl + url.TrimStart('/');
        var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
        request.Headers.Add("User-Agent", AppInfo.UserAgent);
        
        var token = await EnsureValidTokenAsync(ct);
        if (!string.IsNullOrEmpty(token)) 
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        else 
            request.Headers.Add("X-MAL-CLIENT-ID", ApiKeys.MalClientId);

        await ThrottleAsync(ct);
        return await _httpClient.SendAsync(request, ct);
    }

    private DateTime _nextInteractiveTime = DateTime.MinValue;
    private readonly object _interactiveLock = new();

    private async Task InteractiveThrottleAsync(CancellationToken ct)
    {
        TimeSpan delay = TimeSpan.Zero;
        lock (_interactiveLock)
        {
            var now = DateTime.UtcNow;
            if (_nextInteractiveTime < now) _nextInteractiveTime = now;
            delay = _nextInteractiveTime - now;
            _nextInteractiveTime = _nextInteractiveTime.AddMilliseconds(200);
        }
        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
    }

    /// <summary>
    /// GET wrapper that performs an HTTP-conditional request via
    /// <see cref="HttpConditionalCache"/>. Used for endpoints whose body is
    /// safe to replay across the same access token (seasonal / search where
    /// embedded user-specific fields like <c>my_list_status</c> are overridden
    /// at the ViewModel layer from the synced user store).
    /// </summary>
    private Task<byte[]?> GetWithCacheAsync(string url, CancellationToken ct = default)
    {
        var fullUrl = url.StartsWith("http") ? url : MalBaseUrl + url.TrimStart('/');

        return _httpCache.SendAsync(
            requestFactory: async innerCt =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                request.Headers.Add("User-Agent", AppInfo.UserAgent);

                var token = await EnsureValidTokenAsync(innerCt);
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                else
                    request.Headers.Add("X-MAL-CLIENT-ID", ApiKeys.MalClientId);

                return request;
            },
            throttle: InteractiveThrottleAsync,
            ct: ct);
    }

    public void Dispose()
    {
        _tokenRefreshLock.Dispose();
        _rateLimiter.Dispose();
    }
}

