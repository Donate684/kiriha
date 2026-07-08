using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.Api;

public enum SyncTaskType
{
    UpdateProgress,
    UpdateStatus,
    UpdateScore,
    FullUpdate,
    Remove
}

public class SyncTask
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public SyncTaskType Type { get; set; }
    public int? Progress { get; set; }
    public UserAnimeStatus? Status { get; set; }
    public int? Score { get; set; }
    public AnimeItem? FullItem { get; set; }
    public int RetryCount { get; set; } = 0;
    public HashSet<string> SuccessfulTrackers { get; set; } = new();
}

/// <summary>
/// Replays pending tracker mutations from <c>sync_tasks</c> on startup and
/// drains a bounded in-memory queue for live mutations enqueued during the
/// session.
///
/// Lifecycle is owned by the host: <see cref="StartAsync"/> kicks off the
/// background loop, <see cref="StopAsync"/> stops accepting new items and
/// awaits the in-flight task to finish (bounded by the host's stop token).
/// Dropped the old <c>Task.Run</c>-in-ctor pattern so DI never silently
/// observes a half-constructed service running work against unrelated
/// dependencies on shutdown.
/// </summary>
public class SyncManager : IHostedService
{
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly ISyncTaskRepository _syncTaskRepo;
    private readonly Data.DatabaseInitializer _dbInit;
    private readonly HistoryService _historyService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly Channel<SyncTask> _queue;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private const int MaxRetries = 5;
    private const int DelayBetweenRequestsMs = 1500;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (int Id, SyncTaskType Type)> _latestTaskIds = new();

    public SyncManager(
        IEnumerable<ITrackerService> trackers,
        ISyncTaskRepository syncTaskRepo,
        Data.DatabaseInitializer dbInit,
        HistoryService historyService,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _trackers = trackers;
        _syncTaskRepo = syncTaskRepo;
        _dbInit = dbInit;
        _historyService = historyService;
        _backgroundTasks = backgroundTasks;
        _queue = Channel.CreateBounded<SyncTask>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loopTask != null) return Task.CompletedTask;
        // Detached from the caller's context - IHostedService.StartAsync is meant to
        // return quickly. ProcessQueueAsync handles its own cancellation via _cts.
        _loopTask = _backgroundTasks.Run("SyncManager.QueueLoop", InitializeAndProcessQueueAsync, _cts.Token);
        return Task.CompletedTask;
    }

    private volatile bool _isStopped = false;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isStopped) return;
        _isStopped = true;
        
        // Refuse new enqueues so the loop can drain whatever is already buffered.
        _queue.Writer.TryComplete();
        // Cancel in-flight HTTP / Task.Delay so the drain doesn't block on a long retry.
        _cts.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* host gave up waiting */ }
            catch (Exception ex) { Log.Warning(ex, "SyncManager: loop task ended with an exception"); }
        }
        _cts.Dispose();
    }

    private async Task InitializeAndProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await _dbInit.InitializationTask;
            ct.ThrowIfCancellationRequested();
            var pendingTasks = await _syncTaskRepo.GetPendingAsync();
            
            // Deduplicate: take the LATEST task of each type per AnimeId to avoid redundant work.
            // If a 'Remove' task exists, it supersedes all prior tasks for that AnimeId.
            var deduplicated = pendingTasks
                .GroupBy(t => t.AnimeId)
                .SelectMany(g =>
                {
                    var tasks = g.OrderBy(x => x.Id).ToList();
                    var lastRemoveIndex = tasks.FindLastIndex(x => x.Type == nameof(SyncTaskType.Remove));
                    if (lastRemoveIndex > 0)
                    {
                        tasks = tasks.Skip(lastRemoveIndex).ToList();
                    }
                    return tasks
                        .GroupBy(x => x.Type)
                        .Select(typeGroup => typeGroup.Last());
                })
                .OrderBy(x => x.Id)
                .ToList();

            int restoredCount = 0;
            foreach (var entity in deduplicated)
            {
                try
                {
                    if (!Enum.TryParse<SyncTaskType>(entity.Type, out var type))
                    {
                        Log.Warning("Skipping sync task {Id} due to invalid type {Type}", entity.Id, entity.Type);
                        continue;
                    }

                    var task = new SyncTask
                    {
                        Id = entity.Id,
                        AnimeId = entity.AnimeId,
                        Type = type,
                        Progress = entity.Progress,
                        Status = entity.Status != null ? StatusMapper.FromDbString(entity.Status) : null,
                        Score = entity.Score,
                        RetryCount = entity.RetryCount
                    };
                    if (!string.IsNullOrEmpty(entity.SuccessfulTrackersJson))
                    {
                        var trackers = JsonSerializer.Deserialize<HashSet<string>>(entity.SuccessfulTrackersJson);
                        if (trackers != null) task.SuccessfulTrackers = trackers;
                    }
                    if (!string.IsNullOrEmpty(entity.Payload))
                    {
                        task.FullItem = JsonSerializer.Deserialize<AnimeItem>(entity.Payload);
                    }
                    _latestTaskIds.AddOrUpdate(task.AnimeId, (task.Id, task.Type), (k, existing) => task.Id > existing.Id ? (task.Id, task.Type) : existing);
                    _queue.Writer.TryWrite(task);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to parse sync task {Id}", entity.Id);
                }
            }
            
            // Remove the tasks we skipped via deduplication
            var skippedIds = pendingTasks.Select(x => x.Id).Except(deduplicated.Select(x => x.Id)).ToList();
            if (skippedIds.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                await _syncTaskRepo.RemoveManyAsync(skippedIds);
            }

            if (restoredCount > 0) Log.Information("Restored {Count} pending sync tasks from database.", restoredCount);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load pending sync tasks from database");
        }

        await ProcessQueueAsync(ct);
    }

    private SyncTaskEntity MapToEntity(SyncTask task)
    {
        return new SyncTaskEntity
        {
            Id = task.Id,
            AnimeId = task.AnimeId,
            Type = task.Type.ToString(),
            Progress = task.Progress,
            Status = task.Status != null ? StatusMapper.ToDbString(task.Status.Value) : null,
            Score = task.Score,
            Payload = task.FullItem != null ? JsonSerializer.Serialize(task.FullItem, (JsonSerializerOptions?)null) : null,
            RetryCount = task.RetryCount,
            SuccessfulTrackersJson = task.SuccessfulTrackers.Count > 0 ? JsonSerializer.Serialize(task.SuccessfulTrackers, (JsonSerializerOptions?)null) : null
        };
    }

    public async Task EnqueueUpdateAsync(int animeId, int progress, UserAnimeStatus? status = null, int? score = null)
    {
        var task = new SyncTask
        {
            AnimeId = animeId,
            Type = SyncTaskType.UpdateProgress,
            Progress = progress,
            Status = status,
            Score = score
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);
        
        _latestTaskIds[animeId] = (task.Id, task.Type);
        try
        {
            await _queue.Writer.WriteAsync(task);
            Log.Information("Sync task enqueued (DB ID: {Id}): UpdateProgress for {AnimeId} to {Progress}", task.Id, animeId, progress);
        }
        catch (ChannelClosedException)
        {
            Log.Information("SyncManager is shutting down, task (DB ID: {Id}) will be processed on next startup.", task.Id);
        }
    }

    public async Task EnqueueRemoveAsync(int animeId)
    {
        var task = new SyncTask
        {
            AnimeId = animeId,
            Type = SyncTaskType.Remove
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        _latestTaskIds[animeId] = (task.Id, task.Type);
        try
        {
            await _queue.Writer.WriteAsync(task);
            Log.Information("Sync task enqueued (DB ID: {Id}): Remove for {AnimeId}", task.Id, animeId);
        }
        catch (ChannelClosedException)
        {
            Log.Information("SyncManager is shutting down, task (DB ID: {Id}) will be processed on next startup.", task.Id);
        }
    }

    public async Task EnqueueFullUpdateAsync(AnimeItem item)
    {
        var task = new SyncTask
        {
            AnimeId = item.Id,
            Type = SyncTaskType.FullUpdate,
            FullItem = item
        };
        var entity = MapToEntity(task);
        task.Id = await _syncTaskRepo.AddAsync(entity);

        _latestTaskIds[item.Id] = (task.Id, task.Type);
        try
        {
            await _queue.Writer.WriteAsync(task);
            Log.Information("Sync task enqueued (DB ID: {Id}): FullUpdate for {AnimeId}", task.Id, item.Id);
        }
        catch (ChannelClosedException)
        {
            Log.Information("SyncManager is shutting down, task (DB ID: {Id}) will be processed on next startup.", task.Id);
        }
    }



    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct))
            {
                while (_queue.Reader.TryRead(out var task))
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
                                    _queue.Writer.TryWrite(task);
                                }, _cts.Token);
                            }
                            else
                            {
                                Log.Warning("Task {TaskId} permanently failed after {MaxRetries} retries", task.Id, MaxRetries);
                                await _syncTaskRepo.RemoveAsync(task.Id);
                                _historyService.AddEntry(task.AnimeId, task.FullItem?.Title ?? $"ID {task.AnimeId}", null, 0, "SyncFailed", UIUtils.GetLoc("sync.syncing.failed"));
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
        var allTrackers = _trackers.ToList();
        var activeTrackers = allTrackers.Where(t => t.IsEnabled).ToList();

        if (!activeTrackers.Any())
        {
            if (!allTrackers.Any())
            {
                Log.Warning("SyncManager: No trackers registered in the system!");
            }
            else
            {
                var names = string.Join(", ", allTrackers.Select(t => t.Name));
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
