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
    public async Task<List<AnimeStaff>> GetStaffAsync(int malId, MediaKind mediaKind = MediaKind.Anime, EpisodeFreshness freshness = EpisodeFreshness.Default, CancellationToken ct = default)
    {
        if (mediaKind != MediaKind.Anime) return new List<AnimeStaff>();

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
