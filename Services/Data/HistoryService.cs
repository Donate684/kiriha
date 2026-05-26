using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Data;

public class HistoryService
{
    private readonly IHistoryRepository _repo;
    // Tracks in-flight AddEntryAsync calls launched via the fire-and-forget AddEntry overload.
    // FlushAsync awaits all of them so a shutdown can't drop the last few scrobble records.
    private readonly ConcurrentDictionary<Task, byte> _pendingWrites = new();

    public HistoryService(IHistoryRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<HistoryItem>> GetHistoryAsync(int limit = 1000)
    {
        try
        {
            return await _repo.GetAsync(limit);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get history from database");
            return new List<HistoryItem>();
        }
    }

    public async Task AddEntryAsync(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null)
    {
        try
        {
            int typeId = actionType switch
            {
                "Watched" => 1,
                "Reverted" => 2,
                "SyncFailed" => 3,
                "Scrobbled" => 4,
                "ScoreSet" => 5,
                "Completed" => 6,
                "Dropped" => 7,
                _ => 0
            };

            var entry = new HistoryItem
            {
                AnimeId = animeId,
                AnimeTitle = title,
                RussianTitle = russianTitle,
                Episode = episode,
                Timestamp = DateTime.Now,
                ActionType = typeId,
                Detail = detail?.ToString() ?? ""
            };

            await _repo.AddAsync(entry);
            Log.Debug("History entry added for {Id} {Title} (Ep {Ep})", animeId, title, episode);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add history entry for {Title}", title);
        }
    }

    public void AddEntry(int animeId, string title, string? russianTitle, int episode, string actionType = "Watched", object? detail = null)
    {
        var task = AddEntryAsync(animeId, title, russianTitle, episode, actionType, detail);
        if (task.IsCompleted) return; // Synchronous fast-path: nothing to track.

        _pendingWrites.TryAdd(task, 0);
        // Best-effort cleanup so the dictionary doesn't grow unbounded across the
        // session. The continuation runs on the threadpool when SaveChanges resolves.
        task.ContinueWith(t => _pendingWrites.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Awaits every fire-and-forget AddEntry that hasn't yet committed to the DB.
    /// Call this on application shutdown BEFORE DatabaseInitializer.FlushAsync so the WAL
    /// checkpoint sees the final scrobble/ScoreSet/Reverted entries.
    /// </summary>
    public async Task FlushAsync(TimeSpan? timeout = null)
    {
        var pending = _pendingWrites.Keys.ToArray();
        if (pending.Length == 0) return;

        try
        {
            var all = Task.WhenAll(pending);
            if (timeout.HasValue)
            {
                var done = await Task.WhenAny(all, Task.Delay(timeout.Value));
                if (done != all)
                {
                    Log.Warning("HistoryService: FlushAsync timed out with {Count} pending writes", pending.Length);
                    return;
                }
            }
            else
            {
                await all;
            }
            Log.Debug("HistoryService: Flushed {Count} pending writes", pending.Length);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HistoryService: FlushAsync observed an exception in a pending write");
        }
    }
}
