using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repositories;

/// <summary>
/// Persistence boundary for episode release lists (the <c>episode_releases</c>
/// table) and their freshness sidecar (<c>episode_list_meta</c>). Episodes and
/// their fetch timestamp are written together in <see cref="ReplaceAsync"/>
/// so freshness gates can rely on a single atomic boundary instead of two
/// independent rows that could disagree after a crash.
/// </summary>
public interface IEpisodeReleaseRepository
{
    Task<List<EpisodeRelease>> GetByMalIdAsync(int malId);

    /// <summary>UTC timestamp of the last successful fetch, or null on miss.</summary>
    Task<DateTime?> GetFetchedAtAsync(int malId);

    /// <summary>
    /// Replaces the entire episode list for <paramref name="malId"/> and stamps
    /// <see cref="EpisodeListMeta.FetchedAt"/> in the same SaveChanges call.
    /// </summary>
    Task ReplaceAsync(int malId, IEnumerable<EpisodeRelease> episodes);
}

public sealed class EpisodeReleaseRepository : IEpisodeReleaseRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EpisodeReleaseRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<EpisodeRelease>> GetByMalIdAsync(int malId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.EpisodeReleases.AsNoTracking()
            .Where(x => x.MalId == malId)
            .ToListAsync();
    }

    public async Task<DateTime?> GetFetchedAtAsync(int malId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var meta = await context.EpisodeListMeta.AsNoTracking()
            .FirstOrDefaultAsync(m => m.MalId == malId);
        return meta?.FetchedAt;
    }

    public async Task ReplaceAsync(int malId, IEnumerable<EpisodeRelease> episodes)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.EpisodeReleases.Where(x => x.MalId == malId).ToListAsync();
        context.EpisodeReleases.RemoveRange(existing);
        await context.EpisodeReleases.AddRangeAsync(episodes);

        var meta = await context.EpisodeListMeta.FirstOrDefaultAsync(m => m.MalId == malId);
        var now = DateTime.UtcNow;
        if (meta == null)
            context.EpisodeListMeta.Add(new EpisodeListMeta { MalId = malId, FetchedAt = now });
        else
            meta.FetchedAt = now;

        await context.SaveChangesAsync();
    }
}
