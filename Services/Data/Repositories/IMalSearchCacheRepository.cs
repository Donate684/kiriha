using System;
using System.Threading.Tasks;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repositories;

/// <summary>
/// Persistence boundary for the titleÃ¢â€ â€™MAL-id resolution cache (the
/// <c>mal_search_cache</c> table). Built on top of MAL's title search to skip
/// the round-trip when the same window-title text has already been resolved
/// recently.
///
/// TTL policy lives here, not at the call site:
///   * positive resolutions (anime_id != 0) Ã¢â‚¬â€ 30 days
///   * negative resolutions (anime_id == 0) Ã¢â‚¬â€ 7 days, since titles we couldn't
///     match might appear on MAL later (newly added entries) and we want to
///     retry sooner.
/// </summary>
public interface IMalSearchCacheRepository
{
    /// <summary>Returns a non-expired cache hit, or null on miss / expired entry.</summary>
    Task<MalSearchCache?> GetAsync(string queryNormalized);

    Task UpsertAsync(string queryNormalized, int animeId, float score);
}

public sealed class MalSearchCacheRepository : IMalSearchCacheRepository
{
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(7);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MalSearchCacheRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<MalSearchCache?> GetAsync(string queryNormalized)
    {
        if (string.IsNullOrWhiteSpace(queryNormalized)) return null;
        using var context = await _contextFactory.CreateDbContextAsync();
        var entry = await context.MalSearchCache.AsNoTracking()
            .FirstOrDefaultAsync(e => e.QueryNormalized == queryNormalized);
        if (entry == null) return null;

        var ttl = entry.AnimeId == 0 ? NegativeTtl : PositiveTtl;
        if (DateTime.UtcNow - entry.CreatedAt > ttl) return null;

        return entry;
    }

    public async Task UpsertAsync(string queryNormalized, int animeId, float score)
    {
        if (string.IsNullOrWhiteSpace(queryNormalized)) return;
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.MalSearchCache
            .FirstOrDefaultAsync(e => e.QueryNormalized == queryNormalized);
        var now = DateTime.UtcNow;
        if (existing == null)
        {
            context.MalSearchCache.Add(new MalSearchCache
            {
                QueryNormalized = queryNormalized,
                AnimeId = animeId,
                Score = score,
                CreatedAt = now
            });
        }
        else
        {
            existing.AnimeId = animeId;
            existing.Score = score;
            existing.CreatedAt = now;
        }
        await context.SaveChangesAsync();
    }
}
