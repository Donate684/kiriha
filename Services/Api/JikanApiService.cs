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
using Kiriha.Services.Data;
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

public class JikanApiService
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
        QueueLimit = int.MaxValue,
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
    }

    public Task<List<EpisodeRelease>> GetEpisodeListAsync(int malId, CancellationToken ct = default)
        => GetEpisodeListAsync(malId, EpisodeFreshness.Default, ct);

    public async Task<List<EpisodeRelease>> GetEpisodeListAsync(int malId, EpisodeFreshness freshness, CancellationToken ct = default)
    {
        // Cache gate. ForceRefresh skips it; Completed accepts any prior
        // fetch as fresh; Default uses a 12 h sliding window.
        if (freshness != EpisodeFreshness.ForceRefresh)
        {
            try
            {
                var fetchedAt = await _episodes.GetFetchedAtAsync(malId);
                if (fetchedAt != null)
                {
                    bool fresh = freshness == EpisodeFreshness.Completed
                                 || (DateTime.UtcNow - fetchedAt.Value < DefaultTtl);
                    if (fresh)
                    {
                        var cached = await _episodes.GetByMalIdAsync(malId);
                        Log.Debug("Jikan: returning cached episode list for ID {Id} ({Count} entries, age {Age})",
                            malId, cached.Count, DateTime.UtcNow - fetchedAt.Value);
                        return cached;
                    }
                }
            }
            catch (Exception ex)
            {
                // Cache failure must never block live fetches; just log and
                // fall through to the network path.
                Log.Debug(ex, "Jikan: cache lookup failed for ID {Id}", malId);
            }
        }

        var result = new List<EpisodeRelease>();
        // Jikan paginates episodes (100 per page). Long-running shows (One Piece, etc.)
        // need follow-up pages, otherwise we silently truncate the episode list.
        const int MaxPages = 50; // hard safety cap (~5000 episodes)
        int page = 1;
        bool hasNext = true;
        while (hasNext && page <= MaxPages)
        {
            ct.ThrowIfCancellationRequested();
            using var json = await GetJsonAsync($"anime/{malId}/episodes?page={page}", ct);
            if (json == null) break;

            if (json.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var ep in data.EnumerateArray())
                {
                    var epNum = ep.GetProperty("mal_id").GetInt32();
                    var title = ep.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var airDateStr = ep.TryGetProperty("aired", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetString() : null;
                    DateTime? airDate = DateTime.TryParse(airDateStr, out var date) ? date : null;

                    result.Add(new EpisodeRelease
                    {
                        MalId = malId,
                        EpisodeNumber = epNum,
                        Title = title,
                        AirDate = airDate
                    });
                }
            }

            hasNext = json.RootElement.TryGetProperty("pagination", out var pag)
                      && pag.TryGetProperty("has_next_page", out var hn)
                      && hn.ValueKind == JsonValueKind.True;
            page++;
        }
        Log.Information("Jikan: Parsed {Count} episodes for ID {Id} across {Pages} page(s)", result.Count, malId, page - 1);

        // Persist + stamp meta atomically. We only do this on a non-empty
        // result: if the fetch returned zero episodes (e.g. transient 5xx
        // serving an empty body), we don't want to wipe the existing cache.
        if (result.Count > 0)
        {
            try { await _episodes.ReplaceAsync(malId, result); }
            catch (Exception ex) { Log.Debug(ex, "Jikan: failed to persist episode list for ID {Id}", malId); }
        }

        return result;
    }

    public Task<int?> GetLatestEpisodeFromForumAsync(int malId, CancellationToken ct = default)
        => GetLatestEpisodeFromForumAsync(malId, EpisodeFreshness.Default, ct);

    /// <summary>
    /// Fetches the highest "Episode N" topic number from the MAL forum for
    /// <paramref name="malId"/>. Used as a tertiary signal alongside the
    /// official episode list and Shikimori metadata.
    ///
    /// Cache policy:
    ///   * <see cref="EpisodeFreshness.Default"/>     — 12 h TTL (in-memory).
    ///   * <see cref="EpisodeFreshness.Completed"/>   — short-circuits to null
    ///     without hitting the network: completed series gain no value from
    ///     forum scraping and we don't want to burn the 1 RPS budget on them.
    ///   * <see cref="EpisodeFreshness.ForceRefresh"/> — bypasses the gate.
    /// </summary>
    public async Task<int?> GetLatestEpisodeFromForumAsync(int malId, EpisodeFreshness freshness, CancellationToken ct = default)
    {
        // Forum is only meaningful for currently-airing shows. For Completed
        // we skip the round-trip entirely — the episode list cache (infinite
        // TTL) already has the canonical answer.
        if (freshness == EpisodeFreshness.Completed) return null;

        if (freshness != EpisodeFreshness.ForceRefresh
            && _forumCache.TryGetValue(malId, out var hit)
            && DateTime.UtcNow - hit.FetchedAt < DefaultTtl)
        {
            Log.Debug("Jikan: returning cached forum result for ID {Id} (age {Age})",
                malId, DateTime.UtcNow - hit.FetchedAt);
            return hit.Value;
        }

        using var json = await GetJsonAsync($"anime/{malId}/forum", ct);

        int maxEp = 0;
        if (json != null && json.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var topic in data.EnumerateArray())
            {
                var title = topic.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (!string.IsNullOrEmpty(title))
                {
                    // Match ONLY the official MAL pattern "<Title> Episode N Discussion".
                    // The loose `Episode\s+\d+` used to catch spoiler/theory threads like
                    // "Episode 10 will be an original episode..." and inflate EpisodesAired.
                    var match = System.Text.RegularExpressions.Regex.Match(title, @"\bEpisode\s+(\d+)\s+Discussion\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int epNum))
                    {
                        if (epNum > maxEp) maxEp = epNum;
                    }
                }
            }
        }

        int? result = maxEp > 0 ? maxEp : null;

        // Persist even null results so we don't keep retrying forum on shows
        // whose topics don't follow the "Episode N" naming convention. The TTL
        // still ensures we re-check periodically.
        // Skip caching only if the network call itself failed (json == null),
        // so a transient outage doesn't poison the cache for 12 h.
        if (json != null)
        {
            _forumCache[malId] = (result, DateTime.UtcNow);
        }

        return result;
    }

    public async Task<List<AnimeRelation>> GetRelationsAsync(int malId, EpisodeFreshness freshness = EpisodeFreshness.Default, CancellationToken ct = default)
    {
        if (freshness != EpisodeFreshness.ForceRefresh)
        {
            try
            {
                var fetchedAt = await _relations.GetFetchedAtAsync(malId);
                if (fetchedAt != null)
                {
                    // For relations, default TTL is long (7 days) because they rarely change.
                    bool fresh = freshness == EpisodeFreshness.Completed
                                 || (DateTime.UtcNow - fetchedAt.Value < TimeSpan.FromDays(7));
                    if (fresh)
                    {
                        var cached = await _relations.GetBySourceIdAsync(malId);
                        Log.Debug("Jikan: returning cached relations list for ID {Id} ({Count} entries, age {Age})",
                            malId, cached.Count, DateTime.UtcNow - fetchedAt.Value);
                        return cached;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Jikan: cache lookup failed for relations ID {Id}", malId);
            }
        }

        using var json = await GetJsonAsync($"anime/{malId}/relations", ct);
        if (json == null) return new List<AnimeRelation>();

        var result = new List<AnimeRelation>();
        if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var relationNode in data.EnumerateArray())
            {
                var relationType = relationNode.TryGetProperty("relation", out var r) ? r.GetString() ?? "Unknown" : "Unknown";
                if (relationNode.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entryNode in entries.EnumerateArray())
                    {
                        var targetId = entryNode.TryGetProperty("mal_id", out var id) ? id.GetInt32() : 0;
                        var targetType = entryNode.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "";
                        var targetName = entryNode.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
                        var targetUrl = entryNode.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "";

                        result.Add(new AnimeRelation
                        {
                            SourceMalId = malId,
                            RelationType = relationType,
                            TargetMalId = targetId,
                            TargetType = targetType,
                            TargetName = targetName,
                            TargetUrl = targetUrl
                        });
                    }
                }
            }
        }
        
        Log.Information("Jikan: Parsed {Count} relations for ID {Id}", result.Count, malId);

        // Even if empty, we save it so we don't hammer the API for missing relations
        try { await _relations.ReplaceAsync(malId, result); }
        catch (Exception ex) { Log.Debug(ex, "Jikan: failed to persist relations list for ID {Id}", malId); }

        return result;
    }

    public async Task<List<AnimeStaff>> GetStaffAsync(int malId, EpisodeFreshness freshness = EpisodeFreshness.Default, CancellationToken ct = default)
    {
        if (freshness != EpisodeFreshness.ForceRefresh)
        {
            try
            {
                var fetchedAt = await _staff.GetFetchedAtAsync(malId);
                if (fetchedAt != null)
                {
                    // For staff, default TTL is long (7 days) because they rarely change.
                    bool fresh = freshness == EpisodeFreshness.Completed
                                 || (DateTime.UtcNow - fetchedAt.Value < TimeSpan.FromDays(7));
                    if (fresh)
                    {
                        var cached = await _staff.GetBySourceIdAsync(malId);
                        Log.Debug("Jikan: returning cached staff list for ID {Id} ({Count} entries, age {Age})",
                            malId, cached.Count, DateTime.UtcNow - fetchedAt.Value);
                        return cached;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Jikan: cache lookup failed for staff ID {Id}", malId);
            }
        }

        using var json = await GetJsonAsync($"anime/{malId}/staff", ct);
        if (json == null) return new List<AnimeStaff>();

        var result = new List<AnimeStaff>();
        if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var staffNode in data.EnumerateArray())
            {
                if (staffNode.TryGetProperty("person", out var personNode))
                {
                    var personId = personNode.TryGetProperty("mal_id", out var id) ? id.GetInt32() : 0;
                    var personUrl = personNode.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "";
                    var personName = personNode.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
                    
                    string imageUrl = "";
                    if (personNode.TryGetProperty("images", out var imagesNode) && 
                        imagesNode.TryGetProperty("jpg", out var jpgNode) &&
                        jpgNode.TryGetProperty("image_url", out var imgUrlNode))
                    {
                        imageUrl = imgUrlNode.GetString() ?? "";
                    }

                    var positionsList = new List<string>();
                    if (staffNode.TryGetProperty("positions", out var positionsNode) && positionsNode.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pos in positionsNode.EnumerateArray())
                        {
                            var posStr = pos.GetString();
                            if (!string.IsNullOrEmpty(posStr))
                            {
                                positionsList.Add(posStr);
                            }
                        }
                    }

                    result.Add(new AnimeStaff
                    {
                        SourceMalId = malId,
                        PersonMalId = personId,
                        PersonName = personName,
                        PersonUrl = personUrl,
                        PersonImageUrl = imageUrl,
                        Positions = string.Join(", ", positionsList)
                    });
                }
            }
        }
        
        Log.Information("Jikan: Parsed {Count} staff for ID {Id}", result.Count, malId);

        try { await _staff.ReplaceAsync(malId, result); }
        catch (Exception ex) { Log.Debug(ex, "Jikan: failed to persist staff list for ID {Id}", malId); }

        return result;
    }
}
