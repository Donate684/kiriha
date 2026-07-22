using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Serilog;
using System.Net.Http;

namespace Kiriha.Services.Api;

public partial class ShikiApiService
{
    public Task<List<AnimeItem>> SearchAnimeAsync(string query, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AnimeItem>());
    }

    public Task<AnimeItem?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default)
    {
        return Task.FromResult<AnimeItem?>(null);
    }

    public Task<List<AnimeItem>> SearchMangaAsync(string query, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AnimeItem>());
    }

    public Task<AnimeItem?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default)
    {
        return Task.FromResult<AnimeItem?>(null);
    }

    public async Task<ShikiFranchiseResponse?> GetFranchiseAsync(int animeId, CancellationToken ct = default)
    {
        var bytes = await _httpCache.SendAsync(
            requestFactory: _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + $"animes/{animeId}/franchise")),
            ct: ct,
            localTtl: TimeSpan.FromDays(30));

        if (bytes == null) return null;

        try
        {
            return JsonSerializer.Deserialize<ShikiFranchiseResponse>(bytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService: failed to deserialize franchise for {AnimeId}", animeId);
            return null;
        }
    }

    public async Task<ShikiPersonResponse?> GetPersonWorksAsync(int personId, CancellationToken ct = default)
    {
        if (_personCache.TryGetValue(personId, out var hit) && (DateTime.UtcNow - hit.SystemDateTime) < TimeSpan.FromHours(1))
        {
            return hit.Value;
        }

        var bytes = await _httpCache.SendAsync(
            requestFactory: _ => Task.FromResult(new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + $"people/{personId}")),
            ct: ct,
            localTtl: TimeSpan.FromDays(30));

        if (bytes == null) return null;

        try
        {
            var result = JsonSerializer.Deserialize<ShikiPersonResponse>(bytes);
            _personCache[personId] = (result, DateTime.UtcNow);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService: failed to deserialize person data for {PersonId}", personId);
            return null;
        }
    }
}
