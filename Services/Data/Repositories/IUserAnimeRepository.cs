using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data.Repositories;

/// <summary>
/// Persistence boundary for the user's anime list (the <c>user_anime</c> table).
/// Owns full-list synchronisation, point reads/writes, and deletes; intentionally
/// does NOT touch sync tasks or history — those live in their own repositories
/// (<see cref="ISyncTaskRepository"/>, <see cref="IHistoryRepository"/>) so a
/// future move to a different store (e.g. server-backed) can be done one
/// aggregate at a time.
///
/// Lifetime: singleton. The underlying <see cref="IDbContextFactory{TContext}"/>
/// makes every method create a fresh DbContext, so there is no shared mutable
/// state between calls.
/// </summary>
public interface IUserAnimeRepository
{
    Task<List<AnimeItem>> GetAllAsync();
    Task<List<AnimeItem>> GetByMediaKindAsync(MediaKind kind);
    Task UpsertAsync(AnimeItem item);
    Task UpdateAsync(AnimeItem item);
    Task UpdateProgressAsync(AnimeItem item, int progress, UserAnimeStatus? status = null);
    Task UpdateScoreAsync(AnimeItem item, string score);
    Task UpdateMetadataAsync(AnimeItem item);
    Task DeleteAsync(int id);

    /// <summary>
    /// Mirrors a remote tracker snapshot into the local table: upserts items
    /// that exist remotely, deletes items that don't. Refuses to delete a
    /// non-empty local list when the incoming list is empty (defensive against
    /// transient API failures returning an empty body).
    /// </summary>
    Task SyncFromRemoteAsync(IEnumerable<AnimeItem> items);

    /// <summary>Local poster paths for items currently tracked. Used by image cache cleanup.</summary>
    Task<List<string>> GetActiveLocalImagePathsAsync();
}

public sealed class UserAnimeRepository : IUserAnimeRepository
{
    private static readonly TimeSpan ProgressCheckpointInterval = TimeSpan.FromSeconds(10);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private long _lastProgressCheckpointTicks;

    public UserAnimeRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<AnimeItem>> GetAllAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var list = await context.UserAnime.AsNoTracking().ToListAsync();
        Log.Information("Loaded {Count} anime/manga items from database", list.Count);
        return list;
    }

    public async Task<List<AnimeItem>> GetByMediaKindAsync(MediaKind kind)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var list = await context.UserAnime.AsNoTracking().Where(x => x.MediaKind == kind).ToListAsync();
        Log.Information("Loaded {Count} {Kind} items from database", list.Count, kind);
        return list;
    }

    public async Task UpsertAsync(AnimeItem item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == item.Id);
        if (existing == null)
        {
            Log.Information("Inserting new Anime {Title} (ID: {Id})", item.Title, item.Id);
            context.UserAnime.Add(item.Clone());
        }
        else
        {
            item.CopyTo(existing);
            context.UserAnime.Update(existing);
        }
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(AnimeItem item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == item.Id);
        if (existing == null)
        {
            Log.Warning("Attempted to update non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            // Fall back to upsert so the caller's intent is preserved instead of silently dropped.
            await UpsertAsync(item);
            return;
        }

        Log.Information("Updating Anime {Title} (ID: {Id}). Rewatching: {Rewatch}", item.Title, item.Id, item.IsRewatching);
        item.CopyTo(existing);
        context.UserAnime.Update(existing); // explicit because the context is NoTracking
        await context.SaveChangesAsync();

        // Critical write: ensure new progress reaches the main .db file ASAP so a
        // Windows hard shutdown cannot leave us behind the remote tracker. PASSIVE
        // never blocks readers/writers and is cheap when the WAL is small.
        try { await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);"); }
        catch (System.Exception ex) { Log.Warning(ex, "wal_checkpoint(PASSIVE) failed after updating {Title}", item.Title); }

        Log.Information("Successfully saved {Title} to database", item.Title);
    }

    public async Task UpdateProgressAsync(AnimeItem item, int progress, UserAnimeStatus? status = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var shouldUpdateStatus = status.HasValue && status.Value != UserAnimeStatus.None;
        var isManga = item.MediaKind != MediaKind.Anime;
        
        var affected = shouldUpdateStatus
            ? await context.UserAnime
                .Where(x => x.Id == item.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Progress, progress)
                    .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                    .SetProperty(x => x.VolumesRead, item.VolumesRead)
                    .SetProperty(x => x.Status, status!.Value))
            : await context.UserAnime
                .Where(x => x.Id == item.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Progress, progress)
                    .SetProperty(x => x.ChaptersRead, item.ChaptersRead)
                    .SetProperty(x => x.VolumesRead, item.VolumesRead));

        if (affected == 0)
        {
            Log.Warning("Attempted to update progress for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item);
            return;
        }

        await CheckpointProgressWriteAsync(context, item);
    }

    public async Task UpdateScoreAsync(AnimeItem item, string score)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Score, score));

        if (affected == 0)
        {
            Log.Warning("Attempted to update score for non-existent anime {Title} (ID: {Id})", item.Title, item.Id);
            await UpsertAsync(item);
        }
    }

    public async Task UpdateMetadataAsync(AnimeItem item)
    {
        using var context = await _contextFactory.CreateDbContextAsync();

        var alternativeTitles = new List<string>(item.AlternativeTitles);
        var affected = await context.UserAnime
            .Where(x => x.Id == item.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RussianTitle, item.RussianTitle)
                .SetProperty(x => x.RussianSynopsis, item.RussianSynopsis)
                .SetProperty(x => x.EnglishTitle, item.EnglishTitle)
                .SetProperty(x => x.JapaneseTitle, item.JapaneseTitle)
                .SetProperty(x => x.AlternativeTitles, alternativeTitles));

        if (affected == 0)
        {
            Log.Debug("Skipping metadata-only update for non-user anime {Title} (ID: {Id})", item.Title, item.Id);
        }
    }

    public async Task DeleteAsync(int id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.UserAnime.FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null) return;
        context.UserAnime.Remove(existing);
        await context.SaveChangesAsync();
    }

    public async Task SyncFromRemoteAsync(IEnumerable<AnimeItem> items)
    {
        var incomingItems = items.ToList(); // materialize to avoid multiple evaluations
        var incomingIds = incomingItems.Select(x => x.Id).ToHashSet();

        using var context = await _contextFactory.CreateDbContextAsync();

        // Safety check: if the API returned an empty list while we have meaningful
        // local state, treat it as a transient failure and refuse to wipe.
        if (incomingItems.Count == 0)
        {
            var localCount = await context.UserAnime.CountAsync();
            if (localCount > 10)
            {
                Log.Warning("Sync: Incoming list is empty but local DB has {Count} items. Skipping full deletion for safety.", localCount);
                return;
            }
        }

        // The context runs with NoTracking globally — opt into a transaction so
        // the upsert/delete happens atomically.
        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            var existingItems = await context.UserAnime.ToListAsync();
            var toRemove = existingItems.Where(x => !incomingIds.Contains(x.Id)).ToList();
            if (toRemove.Count > 0)
            {
                context.UserAnime.RemoveRange(toRemove);
                var sample = string.Join(", ", toRemove.Take(10).Select(x => $"{x.Id}:{x.Title}"));
                Log.Information("Sync: Removing {Count} items from DB. Sample: {Sample}", toRemove.Count, sample);
            }

            var existingItemsDict = existingItems.ToDictionary(x => x.Id);

            foreach (var item in incomingItems)
            {
                if (existingItemsDict.TryGetValue(item.Id, out var existing))
                {
                    item.CopyTo(existing);
                    context.UserAnime.Update(existing);
                }
                else
                {
                    context.UserAnime.Add(item.Clone());
                }
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            Log.Information("Sync: Database update completed. Total items in incoming list: {Count}", incomingItems.Count);
        }
        catch (System.Exception ex)
        {
            await transaction.RollbackAsync();
            Log.Error(ex, "Failed to sync anime list to EF Core database");
        }
    }

    public async Task<List<string>> GetActiveLocalImagePathsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.UserAnime
            .AsNoTracking()
            .Where(x => !string.IsNullOrEmpty(x.LocalPosterPath))
            .Select(x => x.LocalPosterPath!)
            .ToListAsync();
    }

    private async Task CheckpointProgressWriteAsync(AppDbContext context, AnimeItem item)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastProgressCheckpointTicks);
        if (lastTicks != 0 && nowTicks - lastTicks < ProgressCheckpointInterval.Ticks)
            return;

        if (Interlocked.CompareExchange(ref _lastProgressCheckpointTicks, nowTicks, lastTicks) != lastTicks)
            return;

        try { await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);"); }
        catch (System.Exception ex) { Log.Warning(ex, "wal_checkpoint(PASSIVE) failed after updating progress for {Title}", item.Title); }
    }
}
