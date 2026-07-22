using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.Api;


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
public partial class SyncManager : IHostedService
{
    private readonly IReadOnlyList<ITrackerService> _trackers;
    private readonly ISyncTaskRepository _syncTaskRepo;
    private readonly Data.DatabaseInitializer _dbInit;
    private readonly HistoryService _historyService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly System.Collections.Concurrent.ConcurrentQueue<SyncTask> _highPriorityQueue = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<SyncTask> _lowPriorityQueue = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
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
        _trackers = trackers.ToList();
        _syncTaskRepo = syncTaskRepo;
        _dbInit = dbInit;
        _historyService = historyService;
        _backgroundTasks = backgroundTasks;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loopTask != null) return Task.CompletedTask;
        _loopTask = _backgroundTasks.Run("SyncManager.QueueLoop", InitializeAndProcessQueueAsync, _cts.Token);
        return Task.CompletedTask;
    }

    private volatile bool _isStopped = false;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isStopped) return;
        _isStopped = true;

        _cts.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* host gave up waiting */ }
            catch (Exception ex) { Log.Warning(ex, "SyncManager: loop task ended with an exception"); }
        }
        _cts.Dispose();
        _queueSignal.Dispose();
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
                    if (lastRemoveIndex >= 0)
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
                    _lowPriorityQueue.Enqueue(task);
                    _queueSignal.Release();
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
            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): UpdateProgress for {AnimeId} to {Progress}", task.Id, animeId, progress);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
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
            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): Remove for {AnimeId}", task.Id, animeId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
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
            _highPriorityQueue.Enqueue(task);
            _queueSignal.Release();
            Log.Information("Sync task enqueued (DB ID: {Id}): FullUpdate for {AnimeId}", task.Id, item.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enqueue task (DB ID: {Id})", task.Id);
        }
    }





}
