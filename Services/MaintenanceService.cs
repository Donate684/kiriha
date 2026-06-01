using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Dialogs;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;
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

    private int _isRunning; // 0/1, manipulated via Interlocked

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
        IBackgroundTaskSupervisor backgroundTasks)
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
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;

        _backgroundTasks.Run("MaintenanceService.Loop", RunMaintenanceLoopAsync).SafeFireAndForget("MaintenanceLoop");
        Log.Information("MaintenanceService: Started background loop");
    }

    private async Task RunMaintenanceLoopAsync(CancellationToken ct)
    {
        // Initial delay to let the app finish startup UI work
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PerformTasksAsync(ct);
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
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
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
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await _dialogs.ShowUpdateDialogAsync(isDownloaded: true);
                        }).SafeFireAndForget("UpdateDialog");
                    }
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
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

    public void Dispose()
    {
        Interlocked.Exchange(ref _isRunning, 0);
    }
}
