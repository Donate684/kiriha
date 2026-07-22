using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.Services.Api;

public partial class MalApiService
{
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
            var bytes = await GetWithCacheAsync($"anime/{animeId}?fields={AnimeFields}&nsfw=true", ct, localTtl: TimeSpan.FromDays(30));
            if (bytes == null) return null;

            using var json = JsonDocument.Parse(bytes);
            return MalMapper.MapJsonToAnimeItem(json.RootElement);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Log.Warning(ex, "MalApiService: GetAnimeDetailsAsync failed for {Id}", animeId); return null; }
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
            var bytes = await GetWithCacheAsync($"manga/{mangaId}?fields={MangaFields}&nsfw=true", ct, localTtl: TimeSpan.FromDays(30));
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
}
