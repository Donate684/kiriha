using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Utils.Async;
using Serilog;
using Serilog;

namespace Kiriha.Services.Maintenance;

public class UpdateMaintenanceTask : IMaintenanceTask
{
    private readonly UpdateService _updateService;
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IDialogService _dialogs;

    public UpdateMaintenanceTask(
        UpdateService updateService,
        SettingsService settingsService,
        NotificationService notificationService,
        IUiDispatcher uiDispatcher,
        IDialogService dialogs)
    {
        _updateService = updateService;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _uiDispatcher = uiDispatcher;
        _dialogs = dialogs;
    }

    public TimeSpan InitialDelay => TimeSpan.FromSeconds(30);
    public TimeSpan Interval => TimeSpan.FromHours(4);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_settingsService.Current.System.AutoCheckUpdates)
            return;

        Log.Debug("MaintenanceTask: Triggering Update check...");
        bool found = await _updateService.CheckForUpdatesAsync(ct);

        if (found)
        {
            if (!string.IsNullOrEmpty(_updateService.NewVersion))
                _notificationService.NotifyAppUpdate(_updateService.NewVersion!);

            if (_settingsService.Current.System.AutoDownloadUpdates)
            {
                Log.Information("MaintenanceTask: New update found, starting auto-download...");
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
}
