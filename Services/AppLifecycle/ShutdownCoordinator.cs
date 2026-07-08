using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Services.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed class ShutdownCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _drainLock = new(1, 1);
    private int _shutdownRequested;
    private int _shutdownReady;
    private int _drained;

    public ShutdownCoordinator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (Volatile.Read(ref _shutdownReady) != 0)
            return;

        e.Cancel = true;

        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
            return;

        await DrainAsync();

        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
            await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown());
    }

    public async Task DrainAsync()
    {
        if (Volatile.Read(ref _drained) != 0)
            return;

        await _drainLock.WaitAsync();
        try
        {
            if (_drained != 0)
                return;

            await StopPlayerResidentAsync();
            await FlushPendingWritesAsync();
            Volatile.Write(ref _drained, 1);
            Volatile.Write(ref _shutdownReady, 1);
        }
        finally
        {
            _drainLock.Release();
        }
    }

    public async Task StopPlayerResidentAsync()
    {
        try
        {
            if (!await WaitForAsync(PlayerProcessBridge.StopResidentAsync(), 700))
                Log.Warning("Shutdown: stopping player resident process exceeded {Ms}ms timeout", 700);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown: failed to stop player resident process");
        }
    }

    public async Task FlushPendingWritesAsync()
    {
        const int hostedStopTimeoutMs = 2000;
        const int backgroundStopTimeoutMs = 2500;
        const int historyTimeoutMs = 2500;
        const int dbTimeoutMs = 2500;

        try
        {
            var supervisor = _serviceProvider.GetService<IBackgroundTaskSupervisor>();
            if (supervisor != null)
            {
                var backgroundDone = await WaitForAsync(async () =>
                {
                    using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(backgroundStopTimeoutMs));
                    await supervisor.StopAsync(stopCts.Token);
                }, backgroundStopTimeoutMs + 500);

                if (!backgroundDone)
                    Log.Warning("Shutdown flush: background tasks exceeded {Ms}ms timeout", backgroundStopTimeoutMs);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: background task stop failed");
        }

        try
        {
            var hostedDone = await WaitForAsync(async () =>
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(hostedStopTimeoutMs));
                foreach (var hosted in _serviceProvider.GetServices<IHostedService>())
                {
                    try
                    {
                        await hosted.StopAsync(stopCts.Token);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Shutdown: hosted service {Type} StopAsync threw", hosted.GetType().Name);
                    }
                }
            }, hostedStopTimeoutMs + 500);

            if (!hostedDone)
                Log.Warning("Shutdown flush: hosted services exceeded {Ms}ms timeout", hostedStopTimeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: hosted services stop failed");
        }

        try
        {
            await _serviceProvider.GetRequiredService<SettingsService>().SaveAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: SettingsService.SaveAsync failed");
        }

        try
        {
            var historyDone = await WaitForAsync(
                _serviceProvider.GetRequiredService<HistoryService>().FlushAsync(TimeSpan.FromSeconds(2)),
                historyTimeoutMs);

            if (!historyDone)
                Log.Warning("Shutdown flush: HistoryService.FlushAsync exceeded {Ms}ms timeout", historyTimeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: HistoryService.FlushAsync failed");
        }

        try
        {
            var dbDone = await WaitForAsync(
                _serviceProvider.GetRequiredService<DatabaseInitializer>().FlushAsync(),
                dbTimeoutMs);

            if (!dbDone)
                Log.Warning("Shutdown flush: DatabaseInitializer.FlushAsync exceeded {Ms}ms timeout", dbTimeoutMs);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Shutdown flush: DatabaseInitializer.FlushAsync failed");
        }
    }

    private static Task<bool> WaitForAsync(Func<Task> operation, int timeoutMs) =>
        WaitForAsync(operation(), timeoutMs);

    private static async Task<bool> WaitForAsync(Task operation, int timeoutMs)
    {
        try
        {
            await operation.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
