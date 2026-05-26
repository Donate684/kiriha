using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repositories;

/// <summary>
/// Persistence boundary for the user-action history (the <c>history</c> table).
/// Append-only from the caller's perspective — purging old entries is the
/// responsibility of <see cref="DatabaseMaintenance"/>, not this repo.
/// </summary>
public interface IHistoryRepository
{
    Task AddAsync(HistoryItem item);

    /// <summary>Most recent <paramref name="limit"/> entries, newest first.</summary>
    Task<List<HistoryItem>> GetAsync(int limit = 1000);
}

public sealed class HistoryRepository : IHistoryRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public HistoryRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AddAsync(HistoryItem item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.History.Add(item);
        await context.SaveChangesAsync();
    }

    public async Task<List<HistoryItem>> GetAsync(int limit = 1000)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.History
            .AsNoTracking()
            .OrderByDescending(h => h.Timestamp)
            .Take(limit)
            .ToListAsync();
    }
}
