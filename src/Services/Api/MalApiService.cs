using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Api;

public partial class MalApiService : ITrackerService, IDisposable
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
    private Task<byte[]?> GetWithCacheAsync(string url, CancellationToken ct = default, TimeSpan? localTtl = null)
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
            ct: ct,
            localTtl: localTtl);
    }

    public void Dispose()
    {
        _tokenRefreshLock.Dispose();
        _rateLimiter.Dispose();
    }
}

