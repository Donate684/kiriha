using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data;

/// <summary>
/// Periodic database hygiene: orphan/expired-row pruning, ANALYZE, conditional
/// VACUUM. Split out of <see cref="DatabaseService"/> so the CRUD path doesn't
/// carry maintenance state (the <c>_lastVacuum</c> field) and so the cadence
/// is owned by a single class instead of scattered across the file.
///
/// Lifecycle: invoked from <see cref="MaintenanceService"/> on its own daily
/// cadence — never block the UI thread on this. VACUUM is gated on
/// freelist size or a 7-day clock to avoid rewriting the whole DB on every run.
/// </summary>
public sealed class DatabaseMaintenance
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private DateTime _lastVacuum = DateTime.MinValue;

    public DatabaseMaintenance(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task PerformAsync()
    {
        Log.Information("DatabaseMaintenance: Starting database maintenance and cleanup...");
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            // 1. Remove orphaned metadata (ShikiMetadata for items not in user list)
            var orphanedMetadataCount = await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM metadata WHERE id NOT IN (SELECT id FROM user_anime)");
            if (orphanedMetadataCount > 0)
                Log.Information("DatabaseMaintenance: Removed {Count} orphaned metadata entries", orphanedMetadataCount);

            // 2. Remove orphaned episode releases + releases for anime that finished
            //    airing more than 30 days ago (no longer needed for tracking).
            var oldEpisodesCount = await context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM episode_releases 
                WHERE mal_id NOT IN (SELECT id FROM user_anime)
                OR mal_id IN (SELECT id FROM user_anime WHERE status_detailed = 'finished_airing' AND airing_date < date('now', '-30 days'))");
            if (oldEpisodesCount > 0)
                Log.Information("DatabaseMaintenance: Removed {Count} outdated or orphaned episode release entries", oldEpisodesCount);

            // 3. Permanently failed sync tasks (>=5 retries). Log them as SyncFailed
            //    in history before deletion so the user can see what was lost. Done inline
            //    via the same DbContext to keep maintenance independent of the
            //    higher-level HistoryService.
            var stuckTasks = await context.SyncTasks
                .Where(t => t.RetryCount >= 5)
                .ToListAsync();
            if (stuckTasks.Count > 0)
            {
                // ActionType 3 == "SyncFailed" (see HistoryService.AddEntryAsync mapping).
                foreach (var t in stuckTasks)
                {
                    context.History.Add(new HistoryItem
                    {
                        AnimeId = t.AnimeId,
                        AnimeTitle = $"ID {t.AnimeId}",
                        RussianTitle = null,
                        Episode = t.Progress ?? 0,
                        Timestamp = DateTime.Now,
                        ActionType = 3,
                        Detail = "SyncFailed (max retries exceeded)"
                    });
                }
                context.SyncTasks.RemoveRange(stuckTasks);
                await context.SaveChangesAsync();
                Log.Information("DatabaseMaintenance: Removed {Count} permanently failed sync tasks", stuckTasks.Count);
            }

            // 4. Limit history (keep only last 180 days)
            var historyCleanupCount = await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM history WHERE timestamp < date('now', '-180 days')");
            if (historyCleanupCount > 0)
                Log.Information("DatabaseMaintenance: Removed {Count} old history entries", historyCleanupCount);

            // 5. Trim file recognition cache to entries used in the last 90 days.
            //    Compares last_used as ISO-8601 string; non-ISO rows are left alone.
            var fileCacheCount = await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM file_recognition_cache WHERE last_used != '' AND last_used < date('now', '-90 days')");
            if (fileCacheCount > 0)
                Log.Information("DatabaseMaintenance: Removed {Count} stale file recognition cache entries", fileCacheCount);

            // 5a. Trim mal_search_cache by TTL: positive entries 30d, negative entries 7d.
            //     Mirrors the TTL policy enforced on read-side in GetMalSearchCacheAsync.
            var malCacheCount = await context.Database.ExecuteSqlRawAsync(@"
                DELETE FROM mal_search_cache
                WHERE (anime_id <> 0 AND created_at < date('now', '-30 days'))
                   OR (anime_id =  0 AND created_at < date('now', '-7 days'))");
            if (malCacheCount > 0)
                Log.Information("DatabaseMaintenance: Removed {Count} expired MAL search cache entries", malCacheCount);

            // 5b. Trim http_response_cache by 30d TTL. Conditional GETs would
            //     refresh fresh-but-old entries on the next access, so the
            //     hard TTL is mostly a defence against orphaned URLs (deleted
            //     seasons, stale paginated cursors, etc.).
            var httpCacheCount = await context.Database.ExecuteSqlRawAsync(
                "DELETE FROM http_response_cache WHERE created_at < date('now', '-30 days')");
            if (httpCacheCount > 0)
                Log.Information("DatabaseMaintenance: Removed {Count} expired HTTP cache entries", httpCacheCount);

            // 6. ANALYZE is cheap and always useful for the query planner.
            await context.Database.ExecuteSqlRawAsync("ANALYZE;");

            // 7. VACUUM is expensive (rewrites the whole DB) — only run when there is
            //    real fragmentation OR no more than once per week. VACUUM also resets
            //    journal_mode to 'delete', so we re-apply WAL afterwards.
            if (await ShouldVacuumAsync(context))
            {
                Log.Information("DatabaseMaintenance: Running VACUUM (db is fragmented or weekly cadence reached)...");
                await context.Database.ExecuteSqlRawAsync("VACUUM;");
                // VACUUM dropped us out of WAL mode; restore it (and our PRAGMAs).
                await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;");
                await context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint=200;");
                _lastVacuum = DateTime.UtcNow;
            }

            Log.Information("DatabaseMaintenance: Database maintenance completed successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DatabaseMaintenance: Error during database maintenance");
        }
    }

    private async Task<bool> ShouldVacuumAsync(AppDbContext context)
    {
        // Run VACUUM if either:
        //   - we haven't done it in 7 days, OR
        //   - the freelist is large (>1000 free pages = a lot of recoverable space).
        if (DateTime.UtcNow - _lastVacuum >= TimeSpan.FromDays(7))
            return true;

        try
        {
            var conn = context.Database.GetDbConnection();
            bool wasClosed = conn.State == ConnectionState.Closed;
            if (wasClosed) await conn.OpenAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA freelist_count;";
                var freelist = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
                return freelist > 1000;
            }
            finally
            {
                if (wasClosed) await conn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DatabaseMaintenance: freelist probe failed, skipping VACUUM this cycle");
            return false;
        }
    }
}
