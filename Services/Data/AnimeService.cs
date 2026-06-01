using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.Services.Data;

public class AnimeService
{
    private const int MinimumStatusGuardCount = 10;
    private const int MinimumStatusDropCount = 5;
    private const double MaximumAllowedStatusDropRatio = 0.30;

    private readonly Repositories.IUserAnimeRepository _userAnimeRepo;
    private readonly Repositories.ISyncTaskRepository _syncTaskRepo;
    private readonly DatabaseInitializer _dbInit;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly SyncManager _syncManager;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted; // Interlocked guard for InitializeAsync
    private int _syncing;     // Interlocked guard for SyncWithTrackersAsync
    private readonly HashSet<int> _recentlyDeletedIds = new();
    public Task InitializationTask => _initTcs.Task;

    public bool IsRecentlyDeleted(int animeId)
    {
        lock (_recentlyDeletedIds) return _recentlyDeletedIds.Contains(animeId);
    }

    // Use BulkObservableCollection so initial population (2000+ items from the
    // local cache on startup) can be done with a single Reset notification
    // instead of one event per Add. AnimeListViewModel and any other consumer
    // sees this as a regular ObservableCollection<AnimeItem>.
    public Kiriha.Utils.BulkObservableCollection<AnimeItem> Collection { get; } = new();
    
    public bool IsInitializing => Volatile.Read(ref _initStarted) == 1 && !_initTcs.Task.IsCompleted;
    public bool IsSyncing => Volatile.Read(ref _syncing) == 1;

    public AnimeService(
        Repositories.IUserAnimeRepository userAnimeRepo,
        Repositories.ISyncTaskRepository syncTaskRepo,
        DatabaseInitializer dbInit,
        IEnumerable<ITrackerService> trackers,
        SyncManager syncManager,
        SettingsService settingsService,
        HistoryService historyService,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher)
    {
        _userAnimeRepo = userAnimeRepo;
        _syncTaskRepo = syncTaskRepo;
        _dbInit = dbInit;
        _trackers = trackers;
        _syncManager = syncManager;
        _settingsService = settingsService;
        _historyService = historyService;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
    }

    public async Task InitializeAsync()
    {
        if (Interlocked.CompareExchange(ref _initStarted, 1, 0) != 0)
        {
            await _initTcs.Task;
            return;
        }

        try
        {
            var total = Stopwatch.StartNew();
            var stage = Stopwatch.StartNew();
            await _dbInit.InitializationTask;
            Log.Information("StartupTiming: anime service waited for database elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            var cached = await _userAnimeRepo.GetAllAsync();
            Log.Information(
                "StartupTiming: cached anime loaded count={Count} elapsedMs={ElapsedMs}",
                cached?.Count ?? 0,
                stage.ElapsedMilliseconds);
            
            // Single-shot Reset instead of per-item Add: one CollectionChanged event
            // for 2000+ items vs. 2000+ events. Eliminates the ~400 ms UI stall
            // observed on startup when seeding the cached anime list.
            stage.Restart();
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (cached != null && cached.Count > 0)
                    Collection.Reset(cached);
                else
                    Collection.Clear();
            });
            Log.Information(
                "StartupTiming: cached anime applied to UI collection count={Count} elapsedMs={ElapsedMs}",
                Collection.Count,
                stage.ElapsedMilliseconds);
            
            if (Collection.Count == 0)
            {
                stage.Restart();
                await SyncWithTrackersAsync();
                Log.Information("StartupTiming: initial tracker sync elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);
            }

            Log.Information("StartupTiming: anime service initialized elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize AnimeService");
        }
        finally
        {
            _initTcs.TrySetResult();
            // Intentionally NOT sending AnimeListRefreshMessage here:
            // - AnimeListViewModel.InitializeAsync already runs UpdateCounts +
            //   ApplyCurrentFilters right after awaiting us, so the message
            //   would just trigger a second redundant pass on 2000+ items
            //   (the source of an extra ~350 ms UI stall on startup).
            // - SeasonalViewModel pulls fresh data on first navigation via
            //   MainWindowViewModel.NavigateSeasonal, so it doesn't need the
            //   notification at startup either.
        }
    }

    public async Task<bool> SyncWithTrackersAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return false;

        var primaryTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
        if (primaryTracker == null)
        {
            Log.Warning("No active trackers found for synchronization.");
            Interlocked.Exchange(ref _syncing, 0);
            return false;
        }

        try
        {
            status?.Report(UIUtils.GetLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserAnimeListAsync(ct);
            if (apiList == null) return false;

            // Sanity guard: if the tracker handed back a suspiciously truncated list (e.g. silent
            // mid-pagination failure that didn't surface as null), refuse to run the destructive
            // diff. Otherwise we'd "remove" the missing half of the user's library from the local
            // DB on every retry. Threshold: lose >30% relative to current cache when cache is
            // non-trivial. The user can always do a full re-sync after restart if this triggers.
            var currentItems = await _uiDispatcher.InvokeAsync(() => Collection.ToList());
            var localCount = currentItems.Count;
            if (localCount >= 50 && apiList.Count < localCount * 0.7)
            {
                Log.Warning("SyncWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return false;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return false;

            await ProcessSyncResults(apiList, currentItems, status, ct);
            
            status?.Report(UIUtils.GetLoc("sync.saving.to_db"));
            // Snapshot Collection on the UI thread to avoid "Collection was modified" if
            // the UI is iterating concurrently. ObservableCollection is not thread-safe.
            var snapshot = await _uiDispatcher.InvokeAsync(() => Collection.ToList());
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot);
            
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to sync with {Tracker}", primaryTracker.Name);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }
    }

    private static bool IsRemoteSnapshotSafe(List<AnimeItem> currentItems, List<AnimeItem> apiList)
    {
        static int CountStatus(List<AnimeItem> items, UserAnimeStatus status)
        {
            return status == UserAnimeStatus.Watching
                ? items.Count(x => x.Status == UserAnimeStatus.Watching || x.IsRewatching)
                : items.Count(x => x.Status == status);
        }

        foreach (var trackedStatus in new[]
        {
            UserAnimeStatus.Watching,
            UserAnimeStatus.Completed,
            UserAnimeStatus.OnHold,
            UserAnimeStatus.Dropped,
            UserAnimeStatus.PlanToWatch
        })
        {
            var local = CountStatus(currentItems, trackedStatus);
            if (local < MinimumStatusGuardCount) continue;

            var incoming = CountStatus(apiList, trackedStatus);
            var dropped = local - incoming;
            if (dropped < MinimumStatusDropCount) continue;

            var incomingRatio = (double)incoming / local;
            if (incomingRatio < MaximumAllowedStatusDropRatio)
            {
                Log.Warning(
                    "SyncWithTrackers: aborting because {Status} count dropped suspiciously from {Local} to {Incoming}. Likely a partial or stale tracker snapshot.",
                    trackedStatus,
                    local,
                    incoming);
                return false;
            }
        }

        return true;
    }

    private async Task ProcessSyncResults(List<AnimeItem> apiList, List<AnimeItem> currentItems, IProgress<string>? status, CancellationToken ct)
    {
        var apiMap = apiList.ToDictionary(x => x.Id);
        
        // Snapshot Collection on UI thread — ObservableCollection is not thread-safe and a
        // background ToList() can race with concurrent UI iteration / Add.
        
        var existingMap = currentItems.ToDictionary(x => x.Id);

        // 1. Remove items no longer in API
        var toRemove = currentItems.Where(x => !apiMap.ContainsKey(x.Id)).ToList();
        if (toRemove.Any())
        {
            await _uiDispatcher.InvokeAsync(() => 
            {
                foreach (var item in toRemove) Collection.Remove(item);
            });
        }

        // 2. Update and Add
        var uiBatch = new List<Action>();
        int total = apiList.Count;

        for (int i = 0; i < total; i++)
        {
            if (ct.IsCancellationRequested) break;
            
            var newItem = apiList[i];
            
            // Check blacklist
            lock (_recentlyDeletedIds)
            {
                if (_recentlyDeletedIds.Contains(newItem.Id)) continue;
            }

            if (existingMap.TryGetValue(newItem.Id, out var existing))
            {
                // We update properties directly on the existing object which is already in the Collection.
                // CopyTo triggers PropertyChanged events that must fire on the UI thread, so the
                // assignment itself is queued into the same batched UI dispatch as Add operations.
                var captured = newItem;
                var capturedExisting = existing;
                uiBatch.Add(() => captured.CopyTo(capturedExisting));
            }
            else
            {
                uiBatch.Add(() => Collection.Add(newItem));
            }

            // Batch adding new items to keep UI smooth
            if (uiBatch.Count >= 50 || i == total - 1)
            {
                if (uiBatch.Count > 0)
                {
                    var currentBatch = uiBatch.ToList();
                    uiBatch.Clear();
                    await _uiDispatcher.InvokeAsync(() => 
                    {
                        foreach (var action in currentBatch) action();
                    });
                }
                
                status?.Report($"{UIUtils.GetLoc("sync.updating.metadata")}: {i + 1}/{total}");
                await Task.Delay(1, ct);
            }
        }
    }

    public async Task AddOrUpdateAnimeAsync(AnimeItem item)
    {
        // If we're adding it back, remove from recently deleted blacklist
        lock (_recentlyDeletedIds) _recentlyDeletedIds.Remove(item.Id);

        // Read Collection and apply CopyTo on the UI thread: ObservableCollection is not thread-safe
        // and AnimeItem property setters raise PropertyChanged that UI bindings must observe on UI.
        var existing = await _uiDispatcher.InvokeAsync(() =>
        {
            var found = Collection.FirstOrDefault(x => x.Id == item.Id);
            if (found != null)
            {
                item.CopyTo(found);
            }
            else
            {
                Collection.Add(item);
            }
            return found;
        });

        await _userAnimeRepo.UpdateAsync(existing ?? item);
    }

    public async Task RemoveAnimeAsync(int animeId)
    {
        // Add to temporary blacklist to prevent re-adding during sync race conditions
        lock (_recentlyDeletedIds) _recentlyDeletedIds.Add(animeId);

        _ = _backgroundTasks.Run("AnimeService.RecentDeleteExpiry", async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
            lock (_recentlyDeletedIds) _recentlyDeletedIds.Remove(animeId);
        });

        // Cancel any pending sync tasks for this anime BEFORE enqueuing the Remove
        // (otherwise CancelTasksForAnime would mark our new task as outdated).
        _syncManager.CancelTasksForAnime(animeId);
        await _syncTaskRepo.RemoveForAnimeAsync(animeId);

        await _uiDispatcher.InvokeAsync(() =>
        {
            var item = Collection.FirstOrDefault(x => x.Id == animeId);
            if (item != null) Collection.Remove(item);
        });

        // Remove locally first so the UI is responsive even when offline.
        await _userAnimeRepo.DeleteAsync(animeId);

        // Persist a Remove sync task so the deletion is replayed against trackers
        // when the network/auth is available again. Without this, an offline delete
        // would silently fail and the next remote sync would re-add the anime.
        try
        {
            await _syncManager.EnqueueRemoveAsync(animeId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeService: Failed to enqueue Remove sync task for {AnimeId}", animeId);
        }
    }

    public async Task<bool> UpdateProgressAsync(AnimeItem item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if ((nextStatus == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Completed) && item.StatusDetailed == "Not yet aired")
        {
            Log.Warning("Cannot set {Title} to {Status} - it has not aired yet.", item.Title, nextStatus);
            return false;
        }

        item.Progress = nextProgress;
        if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None) 
            item.Status = nextStatus.Value;

        await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
        await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress, nextStatus);
        
        return true;
    }

    public async Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeItem item, int nextProgress)
    {
        UserAnimeStatus? nextStatus = null;
        if (item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.Completed)
            nextStatus = UserAnimeStatus.Watching;
        else if (item.TotalEpisodes > 0 && nextProgress >= item.TotalEpisodes && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;

        if (await UpdateProgressAsync(item, nextProgress, nextStatus))
        {
            _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Watched");
            return nextStatus;
        }
        return null;
    }

    public async Task SmartDecrementProgressAsync(AnimeItem item)
    {
        if (item.Progress > 0)
        {
            int nextProgress = item.Progress - 1;
            if (await UpdateProgressAsync(item, nextProgress))
            {
                _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted");
            }
        }
    }

    public async Task SetScoreAsync(AnimeItem item, int score)
    {
        item.Score = score.ToString();
        await _userAnimeRepo.UpdateScoreAsync(item, item.Score);
        await _syncManager.EnqueueUpdateAsync(item.Id, item.Progress, score: score);
        _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, item.Progress, "ScoreSet", score.ToString());
    }
}

