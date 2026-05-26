using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data.Repositories;

/// <summary>
/// Persistence boundary for the offline-replay queue of tracker mutations
/// (the <c>sync_tasks</c> table). <see cref="Services.Api.SyncManager"/> drains
/// these on app start and on a 30-second loop; failed pushes stay here until
/// they hit the retry cap, at which point <see cref="DatabaseMaintenance"/>
/// converts them into <c>SyncFailed</c> history entries.
/// </summary>
public interface ISyncTaskRepository
{
    /// <summary>Persists a new task and returns its assigned id.</summary>
    Task<int> AddAsync(SyncTaskEntity task);

    /// <summary>All currently queued tasks, ordered by id ascending (FIFO).</summary>
    Task<List<SyncTaskEntity>> GetPendingAsync();

    Task UpdateAsync(SyncTaskEntity task);

    /// <summary>Idempotent: a concurrent removal is treated as success.</summary>
    Task RemoveAsync(int id);

    Task RemoveForAnimeAsync(int animeId);
}

public sealed class SyncTaskRepository : ISyncTaskRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SyncTaskRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<int> AddAsync(SyncTaskEntity task)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.SyncTasks.Add(task);
        await context.SaveChangesAsync();
        return task.Id;
    }

    public async Task<List<SyncTaskEntity>> GetPendingAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.SyncTasks.OrderBy(t => t.Id).ToListAsync();
    }

    public async Task UpdateAsync(SyncTaskEntity task)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.SyncTasks.Update(task);
        await context.SaveChangesAsync();
    }

    public async Task RemoveAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var task = new SyncTaskEntity { Id = id };
        context.SyncTasks.Attach(task);
        context.SyncTasks.Remove(task);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Task was already removed by a parallel drain — treat as success.
        }
    }

    public async Task RemoveForAnimeAsync(int animeId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var tasks = await context.SyncTasks.Where(t => t.AnimeId == animeId).ToListAsync();
        if (tasks.Count == 0) return;
        context.SyncTasks.RemoveRange(tasks);
        await context.SaveChangesAsync();
    }
}
