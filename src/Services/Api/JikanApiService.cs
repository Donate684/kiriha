using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models.Entities;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Api;

/// <summary>
/// Freshness policy for <see cref="JikanApiService.GetEpisodeListAsync"/>.
/// Jikan is rate-limited (1 RPS) and the per-page round-trip is ≥ 1.1 s, so
/// long-running shows can take tens of seconds to refresh. Letting callers
/// pick a freshness window lets us skip the live call entirely when the
/// underlying data couldn't possibly have changed.
/// </summary>
public enum EpisodeFreshness
{
    /// <summary>12-hour TTL — appropriate for currently-airing series whose
    /// list grows roughly weekly.</summary>
    Default,
    /// <summary>Effectively infinite TTL — appropriate for finished_airing
    /// series whose episode list is immutable once a series ends.</summary>
    Completed,
    /// <summary>Bypass cache; always hit the live API.</summary>
    ForceRefresh,
}

public partial class JikanApiService : IDisposable
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(12);

    private readonly HttpClient _httpClient;
    private readonly IEpisodeReleaseRepository _episodes;
    private readonly IAnimeRelationRepository _relations;
    private readonly IAnimeStaffRepository _staff;
    private readonly HttpConditionalCache _httpCache;
    // Jikan official limit: 3 RPS / 60 RPM. 1100 ms between calls (~0.9 RPS) keeps us
    // under both windows even when the per-second bucket resets at the end of a minute.
    private readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 100,
        ReplenishmentPeriod = TimeSpan.FromMilliseconds(1100),
        TokensPerPeriod = 1,
        AutoReplenishment = true,
    });
    private const string BaseUrl = "https://api.jikan.moe/v4/";

    // In-memory TTL cache for the /forum endpoint. Forum is only consulted as
    // a fallback signal for currently-airing shows, so a process-lifetime cache
    // is enough — no need to persist across restarts. Keyed by MAL ID; value is
    // (latestEpisodeOrNull, fetchedAtUtc).
    private readonly ConcurrentDictionary<int, (int? Value, DateTime FetchedAt)> _forumCache = new();

    public JikanApiService(HttpClient httpClient, IEpisodeReleaseRepository episodes, IAnimeRelationRepository relations, IAnimeStaffRepository staff, IHttpCacheRepository httpCacheRepo)
    {
        _httpClient = httpClient;
        _episodes = episodes;
        _relations = relations;
        _staff = staff;
        _httpCache = new HttpConditionalCache(httpClient, httpCacheRepo, "Jikan");
    }

    private async Task<JsonDocument?> GetJsonAsync(string endpoint, CancellationToken ct)
    {
        // Conditional GET via HttpConditionalCache + Jikan-specific throttle.
        // The throttle (60 rpm) is enforced for *every* network call regardless of
        // 200/304 — Jikan counts conditional GETs against the same budget.
        var bytes = await _httpCache.SendAsync(
            requestFactory: innerCt =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + endpoint);
                request.Headers.Add("User-Agent", AppInfo.UserAgent);
                return Task.FromResult(request);
            },
            throttle: ThrottleAsync,
            ct: ct);

        if (bytes == null) return null;
        try { return JsonDocument.Parse(bytes); }
        catch (Exception ex)
        {
            Log.Warning(ex, "Jikan: failed to parse JSON for {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// Wait for a token from <see cref="_rateLimiter"/> before allowing a Jikan
    /// HTTP call. AcquireAsync queues serially and respects the caller's cancellation.
    /// </summary>
    private async Task ThrottleAsync(CancellationToken ct)
    {
        using var lease = await _rateLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) throw new HttpRequestException("Rate limit queue exceeded.");
    }



    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
