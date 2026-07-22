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
}
