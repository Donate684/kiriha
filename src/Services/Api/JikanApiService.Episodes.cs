using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.Services.Api;

public partial class JikanApiService
{
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
}
