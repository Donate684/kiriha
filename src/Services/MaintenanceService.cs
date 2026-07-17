using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Core.Dialogs;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using Serilog;

namespace Kiriha.Services;

/// <summary>
/// Centralized service for periodic background maintenance tasks like update checks, 
/// RSS feed polling, and episode airing synchronization.
/// </summary>
public class MaintenanceService : IDisposable
{
    private readonly UpdateService _updateService;
    private readonly RssFeedService _rssService;
    private readonly AiringInfoService _airingService;
    private readonly SettingsService _settingsService;
    private readonly DatabaseMaintenance _dbMaintenance;
    private readonly Data.Repositories.IUserAnimeRepository _userAnimeRepo;
    private readonly ImageCacheService _imageCacheService;
    private readonly NotificationService _notificationService;
    private readonly IDialogService _dialogs;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ShikiMetadataService _shikiMetadata;
    private readonly Data.Repositories.IMetadataRepository _metadataRepo;

    private int _isRunning; // 0/1, manipulated via Interlocked
    private readonly CancellationTokenSource _disposeCts = new();

    // Intervals
    private static readonly TimeSpan RssInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(4);
    private static readonly TimeSpan AiringInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan DbMaintenanceInterval = TimeSpan.FromDays(1);

    // Last run trackers - staggered to avoid startup spikes.
    // Stored as "(interval - stagger) ago" so the next check fires AFTER the stagger from launch.
    // First-run delays from launch: Update ~30s, RSS ~5min, Airing ~5min, DB ~1h.
    private DateTime _lastRssCheck      = DateTime.Now - RssInterval           + TimeSpan.FromMinutes(5);
    private DateTime _lastUpdateCheck   = DateTime.Now - UpdateInterval        + TimeSpan.FromSeconds(30);
    private DateTime _lastAiringSync    = DateTime.Now - AiringInterval        + TimeSpan.FromMinutes(5);
    private DateTime _lastDbMaintenance = DateTime.Now - DbMaintenanceInterval + TimeSpan.FromHours(1);

    public MaintenanceService(
        UpdateService updateService,
        RssFeedService rssService,
        AiringInfoService airingService,
        SettingsService settingsService,
        DatabaseMaintenance dbMaintenance,
        Data.Repositories.IUserAnimeRepository userAnimeRepo,
        ImageCacheService imageCacheService,
        NotificationService notificationService,
        IDialogService dialogs,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher,
        ShikiMetadataService shikiMetadata,
        Data.Repositories.IMetadataRepository metadataRepo)
    {
        _updateService = updateService;
        _rssService = rssService;
        _airingService = airingService;
        _settingsService = settingsService;
        _dbMaintenance = dbMaintenance;
        _userAnimeRepo = userAnimeRepo;
        _imageCacheService = imageCacheService;
        _notificationService = notificationService;
        _dialogs = dialogs;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _shikiMetadata = shikiMetadata;
        _metadataRepo = metadataRepo;
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;

        _backgroundTasks.Run("MaintenanceService.Loop", RunMaintenanceLoopAsync).SafeFireAndForget("MaintenanceLoop");
        _backgroundTasks.Run("MetadataFetcher.Loop", RunMetadataFetcherLoopAsync).SafeFireAndForget("MetadataFetcher");
        Log.Information("MaintenanceService: Started background loop");
    }

    private async Task RunMaintenanceLoopAsync(CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var combinedCt = linkedCts.Token;

        try
        {
            // Initial delay to let the app finish startup UI work
            await Task.Delay(TimeSpan.FromSeconds(5), combinedCt);

            while (!combinedCt.IsCancellationRequested)
            {
                try
                {
                    await PerformTasksAsync(combinedCt);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "MaintenanceService: Error in loop");
                }

                // Sleep for a bit before next check cycle (1 minute is enough for granularity)
                await Task.Delay(TimeSpan.FromMinutes(1), combinedCt);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful exit on cancellation
        }
    }

    private async Task PerformTasksAsync(CancellationToken ct)
    {
        var now = DateTime.Now;

        // 1. RSS Feed Check (High Priority)
        if (now - _lastRssCheck >= RssInterval)
        {
            Log.Debug("MaintenanceService: Triggering RSS check...");
            await _rssService.CheckFeedsAsync();
            _lastRssCheck = now;
        }

        // 2. Airing Info Sync (Medium Priority)
        if (now - _lastAiringSync >= AiringInterval)
        {
            Log.Debug("MaintenanceService: Triggering Airing Info sync...");
            await _airingService.SyncOngoingEpisodesAsync(false, null, ct);
            _lastAiringSync = now;
        }

        // 3. Update Check (Low Priority)
        if (_settingsService.Current.System.AutoCheckUpdates && now - _lastUpdateCheck >= UpdateInterval)
        {
            Log.Debug("MaintenanceService: Triggering Update check...");
            bool found = await _updateService.CheckForUpdatesAsync(ct);
            _lastUpdateCheck = now;

            if (found)
            {
                // Fire OS toast as soon as we know an update is available — this is the
                // earliest signal and works even if the user has the window minimised to tray.
                if (!string.IsNullOrEmpty(_updateService.NewVersion))
                    _notificationService.NotifyAppUpdate(_updateService.NewVersion!);

                if (_settingsService.Current.System.AutoDownloadUpdates)
                {
                    Log.Information("MaintenanceService: New update found, starting auto-download...");
                    bool downloaded = await _updateService.DownloadAndInstallAsync(null, ct);
                    if (downloaded)
                    {
                        _uiDispatcher.InvokeAsync(async () =>
                        {
                            await _dialogs.ShowUpdateDialogAsync(isDownloaded: true);
                        }).SafeFireAndForget("UpdateDialog");
                    }
                }
                else
                {
                    _uiDispatcher.InvokeAsync(async () =>
                    {
                        await _dialogs.ShowUpdateDialogAsync();
                    }).SafeFireAndForget("UpdateDialog");
                }
            }
        }

        // 4. Database & Image Cache Maintenance (Periodic)
        if (now - _lastDbMaintenance >= DbMaintenanceInterval)
        {
            Log.Information("MaintenanceService: Triggering Database and Image Cache maintenance...");
            
            // Step 1: Clean Database (History, Orphaned Metadata, Episode Releases, Stuck Tasks)
            await _dbMaintenance.PerformAsync();

            // Step 2: Get active image paths from DB
            var activePaths = await _userAnimeRepo.GetActiveLocalImagePathsAsync();
            
            // Step 3: Perform smart cleanup of the image folder (delete files NOT in activePaths)
            await _imageCacheService.PerformSmartCleanupAsync(activePaths);
            
            _lastDbMaintenance = now;
        }
    }

    private async Task RunMetadataFetcherLoopAsync(CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var combinedCt = linkedCts.Token;

        try
        {
            // Initial delay
            await Task.Delay(TimeSpan.FromMinutes(2), combinedCt);

            while (!combinedCt.IsCancellationRequested)
            {
                if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), combinedCt);
                    continue;
                }

                try
                {
                    // Scan the entire database
                    var allItems = await _userAnimeRepo.GetAllAsync();
                    var existingIds = await _metadataRepo.GetAllIdsAsync();

                    var missingItems = new System.Collections.Generic.List<Kiriha.Models.AnimeItem>();

                    foreach (var item in allItems)
                    {
                        int cacheId = item.MediaKind switch
                        {
                            Kiriha.Models.Entities.MediaKind.Manga => item.Id | 0x40000000,
                            Kiriha.Models.Entities.MediaKind.LightNovel => item.Id | 0x20000000,
                            _ => item.Id
                        };

                        if (!existingIds.Contains(cacheId))
                        {
                            missingItems.Add(item);
                        }
                    }

                    if (missingItems.Count > 0)
                    {
                        Log.Information("MetadataFetcher: Found {Count} items missing metadata.", missingItems.Count);

                        foreach (var missing in missingItems)
                        {
                            combinedCt.ThrowIfCancellationRequested();

                            if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
                                break;

                            // Wait until window is minimized before fetching
                            await WaitUntilMinimizedAsync(combinedCt);

                            if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
                                break;

                            try
                            {
                                // Fetch metadata one by one
                                var fetched = await _shikiMetadata.GetOrFetchMetadataAsync(missing.Id, null, null, missing.MediaKind);
                                
                                // Ensure main poster is downloaded
                                if (!string.IsNullOrEmpty(missing.MainPictureUrl))
                                {
                                    await _imageCacheService.GetLocalPathOrDownload(missing.MainPictureUrl);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "MetadataFetcher: Failed to fetch metadata or poster for Item {Id}", missing.Id);
                            }

                            // We can't fetch Shiki Relations because they are not part of ShikiMetadata, 
                            // they are fetched via JikanApiService. If the user wants relations, they are usually 
                            // fetched lazily on the Details page. We will just delay.
                            await Task.Delay(TimeSpan.FromSeconds(3), combinedCt);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "MetadataFetcher: Error in processing queue");
                }

                // Wait 1 hour before next full DB scan
                await Task.Delay(TimeSpan.FromHours(1), combinedCt);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful exit
        }
    }
    private async Task WaitUntilMinimizedAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs>? propertyHandler = null;
        EventHandler? closedHandler = null;
        Avalonia.Controls.Window? mainWindow = null;

        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    mainWindow = desktop.MainWindow;
                    if (mainWindow == null || !mainWindow.IsVisible || mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    {
                        tcs.TrySetResult(true);
                        return;
                    }

                    propertyHandler = (_, args) =>
                    {
                        if (args.Property == Avalonia.Visual.IsVisibleProperty || args.Property == Avalonia.Controls.Window.WindowStateProperty)
                        {
                            if (!mainWindow.IsVisible || mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                            {
                                tcs.TrySetResult(true);
                            }
                        }
                    };

                    closedHandler = (_, __) =>
                    {
                        tcs.TrySetResult(true);
                    };

                    mainWindow.PropertyChanged += propertyHandler;
                    mainWindow.Closed += closedHandler;

                    if (!mainWindow.IsVisible || mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    {
                        tcs.TrySetResult(true);
                    }
                }
                else
                {
                    tcs.TrySetResult(true);
                }
            });

            await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task;
        }
        finally
        {
            _uiDispatcher.Post(() =>
            {
                if (mainWindow != null)
                {
                    if (propertyHandler != null) mainWindow.PropertyChanged -= propertyHandler;
                    if (closedHandler != null) mainWindow.Closed -= closedHandler;
                }
            });
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _isRunning, 0);

        try
        {
            if (!_disposeCts.IsCancellationRequested)
            {
                _disposeCts.Cancel();
            }
            _disposeCts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }
    }
}
