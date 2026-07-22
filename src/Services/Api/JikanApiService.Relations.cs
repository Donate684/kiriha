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
    public async Task<List<AnimeRelation>> GetRelationsAsync(int malId, MediaKind mediaKind = MediaKind.Anime, EpisodeFreshness freshness = EpisodeFreshness.Default, CancellationToken ct = default)
    {
        string endpointType = mediaKind == MediaKind.Manga || mediaKind == MediaKind.LightNovel ? "manga" : "anime";
        int cacheId = mediaKind == MediaKind.Anime ? malId : -malId;

        if (freshness != EpisodeFreshness.ForceRefresh)
        {
            try
            {
                var fetchedAt = await _relations.GetFetchedAtAsync(cacheId);
                if (fetchedAt != null)
                {
                    // For relations, default TTL is long (7 days) because they rarely change.
                    bool fresh = freshness == EpisodeFreshness.Completed
                                 || (DateTime.UtcNow - fetchedAt.Value < TimeSpan.FromDays(7));
                    if (fresh)
                    {
                        var cached = await _relations.GetBySourceIdAsync(cacheId);
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

        using var json = await GetJsonAsync($"{endpointType}/{malId}/relations", ct);
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
                            SourceMalId = cacheId,
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
        try { await _relations.ReplaceAsync(cacheId, result); }
        catch (Exception ex) { Log.Debug(ex, "Jikan: failed to persist relations list for ID {Id}", malId); }

        return result;
    }
}
