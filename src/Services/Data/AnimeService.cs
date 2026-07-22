using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.Data;

public partial class AnimeService
{
    private const int MinimumStatusGuardCount = 10;
    private const int MinimumStatusDropCount = 5;
    private const double MaximumAllowedStatusDropRatio = 0.30;
    private const double MaxAllowedTotalDropRatio = 0.70;

    private readonly Repositories.IUserAnimeRepository _userAnimeRepo;
    private readonly Repositories.ISyncTaskRepository _syncTaskRepo;
    private readonly DatabaseInitializer _dbInit;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly SyncManager _syncManager;
    private readonly SettingsService _settingsService;
    private readonly HistoryService _historyService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly RecognitionCache _recognitionCache;
    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted; // Interlocked guard for InitializeAsync
    private int _syncing;     // Interlocked guard for SyncWithTrackersAsync
    private readonly Dictionary<int, CancellationTokenSource> _recentlyDeletedIds = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, AnimeItem> _idIndex = new();
    public Task InitializationTask => _initTcs.Task;

    public bool IsRecentlyDeleted(int animeId)
    {
        lock (_recentlyDeletedIds) return _recentlyDeletedIds.ContainsKey(animeId);
    }

    // Use BulkObservableCollection so initial population (2000+ items from the
    // local cache on startup) can be done with a single Reset notification
    // instead of one event per Add. AnimeListViewModel and any other consumer
    // sees this as a regular ObservableCollection<AnimeItem>.
    public Kiriha.Utils.Collections.BulkObservableCollection<AnimeItem> Collection { get; } = new();

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
        IUiDispatcher uiDispatcher,
        RecognitionCache recognitionCache)
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
        _recognitionCache = recognitionCache;
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
                {
                    Collection.Reset(cached);
                    _idIndex.Clear();
                    foreach (var item in cached) _idIndex[item.Id] = item;
                }
                else
                {
                    Collection.Clear();
                    _idIndex.Clear();
                }
            });

            // Build the recognition cache using background thread
            await Task.Run(() => _recognitionCache.BuildIndex(Collection));

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


}

