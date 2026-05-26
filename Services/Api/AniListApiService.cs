using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Api;

public sealed record AniListAiringInfo(
    int AniListId,
    int MalId,
    int NextEpisode,
    DateTime NextEpisodeAt,
    int? TotalEpisodes);

public class AniListApiService
{
    private const string Endpoint = "https://graphql.anilist.co";
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan EmptyTtl = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IHttpCacheRepository _cache;
    private readonly RateLimiter _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = int.MaxValue,
        ReplenishmentPeriod = TimeSpan.FromMilliseconds(2200),
        TokensPerPeriod = 1,
        AutoReplenishment = true,
    });

    public AniListApiService(HttpClient httpClient, IHttpCacheRepository cache)
    {
        _httpClient = httpClient;
        _cache = cache;
    }

    public async Task<AniListAiringInfo?> GetNextAiringAsync(int malId, bool force = false, CancellationToken ct = default)
    {
        if (malId <= 0) return null;

        var cacheKey = CacheKey(malId);
        if (!force)
        {
            var cached = await TryReadCacheAsync(cacheKey);
            if (cached.Fresh) return cached.Value;
        }

        using var lease = await _rateLimiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired) return null;

        var payload = new AniListGraphQlRequest(
            Query: """
                   query ($malId: Int) {
                     Media(idMal: $malId, type: ANIME) {
                       id
                       idMal
                       episodes
                       nextAiringEpisode {
                         episode
                         airingAt
                       }
                     }
                   }
                   """,
            Variables: new AniListVariables(malId));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("User-Agent", AppInfo.UserAgent);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("AniList: request for MAL {MalId} returned {Status}", malId, response.StatusCode);
                return (await TryReadCacheAsync(cacheKey, allowStale: true)).Value;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var result = ParseAiringInfo(json.RootElement, malId);
            await WriteCacheAsync(cacheKey, result);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "AniList: failed to fetch next airing for MAL {MalId}", malId);
            return (await TryReadCacheAsync(cacheKey, allowStale: true)).Value;
        }
    }

    private async Task<(bool Fresh, AniListAiringInfo? Value)> TryReadCacheAsync(string cacheKey, bool allowStale = false)
    {
        try
        {
            var entry = await _cache.GetAsync(cacheKey);
            if (entry == null || entry.Body.Length == 0) return (false, null);

            var cached = JsonSerializer.Deserialize<AniListAiringCacheEntry>(entry.Body, JsonOptions);
            if (cached == null) return (false, null);

            var age = DateTime.UtcNow - entry.CreatedAt;
            var ttl = cached.Value == null ? EmptyTtl : DefaultTtl;
            if (!allowStale && age > ttl) return (false, null);
            return (true, cached.Value);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AniList: cache lookup failed");
            return (false, null);
        }
    }

    private async Task WriteCacheAsync(string cacheKey, AniListAiringInfo? value)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new AniListAiringCacheEntry(value), JsonOptions);
            await _cache.UpsertAsync(cacheKey, etag: null, lastModified: null, bytes);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AniList: failed to persist airing cache");
        }
    }

    private static AniListAiringInfo? ParseAiringInfo(JsonElement root, int requestedMalId)
    {
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("Media", out var media)
            || media.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (!media.TryGetProperty("nextAiringEpisode", out var next)
            || next.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        if (!next.TryGetProperty("episode", out var epElement) || !epElement.TryGetInt32(out var episode) || episode <= 0)
            return null;

        if (!next.TryGetProperty("airingAt", out var atElement) || !atElement.TryGetInt64(out var airingAt) || airingAt <= 0)
            return null;

        var aniListId = media.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id) ? id : 0;
        var malId = media.TryGetProperty("idMal", out var malElement) && malElement.TryGetInt32(out var parsedMalId)
            ? parsedMalId
            : requestedMalId;
        var totalEpisodes = media.TryGetProperty("episodes", out var epsElement) && epsElement.TryGetInt32(out var eps)
            ? eps
            : (int?)null;
        var nextAt = DateTimeOffset.FromUnixTimeSeconds(airingAt).LocalDateTime;

        return new AniListAiringInfo(aniListId, malId, episode, nextAt, totalEpisodes);
    }

    private static string CacheKey(int malId)
    {
        var raw = $"AniList:nextAiring:{malId}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(raw), hash);
        return Convert.ToHexString(hash);
    }

    private sealed record AniListGraphQlRequest(string Query, AniListVariables Variables);
    private sealed record AniListVariables(int MalId);
    private sealed record AniListAiringCacheEntry(AniListAiringInfo? Value);
}
