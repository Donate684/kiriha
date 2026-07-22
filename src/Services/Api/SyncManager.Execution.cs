using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.Services.Api;

public partial class SyncManager
{
    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _queueSignal.WaitAsync(ct);
                if (_highPriorityQueue.TryDequeue(out var task) || _lowPriorityQueue.TryDequeue(out task))
                {
                    bool executed = true;
                    try
                    {
                        var (success, didExecute) = await ExecuteTaskAsync(task, ct);
                        executed = didExecute;

                        if (success)
                        {
                            await _syncTaskRepo.RemoveAsync(task.Id);
                            Log.Information("Sync task {TaskId} completed successfully.", task.Id);
                        }
                        else
                        {
                            task.RetryCount++;
                            if (task.RetryCount < MaxRetries)
                            {
                                int delayMin = (int)Math.Pow(2, task.RetryCount); // 2, 4, 8, 16 min...
                                Log.Warning("Task {TaskId} failed (attempt {Attempt}/{Max}), will retry in {Delay} min.",
                                    task.Id, task.RetryCount, MaxRetries, delayMin);

                                await _syncTaskRepo.UpdateAsync(MapToEntity(task));

                                // Fire and forget a delayed re-enqueue to not block the main queue
                                _ = _backgroundTasks.Run("SyncManager.DelayedRetry", async retryCt =>
                                {
                                    await Task.Delay(TimeSpan.FromMinutes(delayMin), retryCt);
                                    _lowPriorityQueue.Enqueue(task);
                                    _queueSignal.Release();
                                }, _cts.Token);
                            }
                            else
                            {
                                Log.Warning("Task {TaskId} permanently failed after {MaxRetries} retries", task.Id, MaxRetries);
                                await _syncTaskRepo.RemoveAsync(task.Id);
                                _historyService.AddEntry(task.AnimeId, task.FullItem?.Title ?? $"ID {task.AnimeId}", null, 0, "SyncFailed", Core.UIUtils.GetLoc("sync.syncing.failed"));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error processing sync task {Id}", task.Id);
                    }

                    if (executed)
                    {
                        await Task.Delay(DelayBetweenRequestsMs, ct);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error(ex, "SyncManager error"); }
    }

    private async Task<(bool Success, bool Executed)> ExecuteTaskAsync(SyncTask task, CancellationToken ct)
    {
        if (_latestTaskIds.TryGetValue(task.AnimeId, out var latest) && latest.Id > task.Id)
        {
            if (latest.Type == SyncTaskType.Remove)
            {
                Log.Information("SyncManager: Skipping outdated task {TaskId} ({TaskType}) for Anime {AnimeId} because newer task {LatestId} is Remove.", task.Id, task.Type, task.AnimeId, latest.Id);
                return (true, false);
            }

            // Optimization: If a newer FULL update is pending, we can skip this task.
            // BUT: If this is a FullUpdate and the latest is just a Progress update, we SHOULD NOT skip,
            // because the FullUpdate might contain data (notes/dates) that Progress update doesn't have.

            // We only skip if the newer task is of the same or "broader" type.
            // In our case, FullUpdate is the broadest. Remove must always run - it's the
            // user-facing intent and skipping it would silently keep the entry on the tracker.
            if (task.Type != SyncTaskType.FullUpdate && task.Type != SyncTaskType.Remove)
            {
                Log.Information("SyncManager: Skipping outdated task {TaskId} for Anime {AnimeId} because newer task {LatestId} is pending.", task.Id, task.AnimeId, latest.Id);
                return (true, false);
            }
        }

        bool overallSuccess = true;
        var activeTrackers = _trackers.Where(t => t.IsEnabled).ToList();

        if (activeTrackers.Count == 0)
        {
            if (_trackers.Count == 0)
            {
                Log.Warning("SyncManager: No trackers registered in the system!");
            }
            else
            {
                var names = string.Join(", ", _trackers.Select(t => t.Name));
                Log.Warning("SyncManager: No active (logged in) trackers for sync sync task. Registered trackers: {Trackers}", names);
            }
            return (true, false); // Nothing to do; count as success to avoid retrying.
        }

        bool executedAny = false;
        foreach (var tracker in activeTrackers)
        {
            if (task.SuccessfulTrackers.Contains(tracker.Name))
            {
                Log.Debug("Skipping {Tracker} for task {Id} as it was already successful", tracker.Name, task.Id);
                continue;
            }

            executedAny = true;
            try
            {
                SyncOutcome outcome = SyncOutcome.Success;
                switch (task.Type)
                {
                    case SyncTaskType.UpdateProgress:
                        outcome = await tracker.UpdateProgressAsync(
                            task.AnimeId,
                            task.Progress ?? 0,
                            task.Status,
                            task.Score,
                            task.FullItem?.IsRewatching,
                            task.FullItem?.RewatchCount,
                            ct);
                        break;
                    case SyncTaskType.FullUpdate:
                        if (task.FullItem != null)
                            outcome = await tracker.SaveFullListStatusAsync(task.FullItem, ct);
                        break;
                    case SyncTaskType.Remove:
                        outcome = await tracker.RemoveAnimeAsync(task.AnimeId, ct);
                        break;
                }

                // Both Success and PermanentFailure are "resolved" - add the tracker
                // to SuccessfulTrackers so the next retry pass skips it. The DB schema
                // calls the column SuccessfulTrackers but the semantic is really
                // "trackers we should not call again for this task".
                if (outcome == SyncOutcome.Success || outcome == SyncOutcome.PermanentFailure)
                {
                    task.SuccessfulTrackers.Add(tracker.Name);
                }
                else
                {
                    // TransientFailure - leave out of SuccessfulTrackers, mark task for retry.
                    overallSuccess = false;
                }

                Log.Information("{Tracker} sync for {Id}: {Outcome}", tracker.Name, task.AnimeId, outcome);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error syncing with {Tracker}", tracker.Name);
                overallSuccess = false;
            }
        }

        return (overallSuccess, executedAny);
    }
}
