using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Infrastructure;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Maintenance;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.Services;

/// <summary>
/// Centralized service for periodic background maintenance tasks like update checks, 
/// RSS feed polling, and episode airing synchronization.
/// </summary>
public class MaintenanceService : IDisposable
{
    private readonly IEnumerable<IMaintenanceTask> _tasks;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _isRunning;

    public MaintenanceService(
        IEnumerable<IMaintenanceTask> tasks,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _tasks = tasks;
        _backgroundTasks = backgroundTasks;
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;

        foreach (var task in _tasks)
        {
            _backgroundTasks.Run($"MaintenanceTask.{task.GetType().Name}", ct => RunTaskLoopAsync(task, ct))
                .SafeFireAndForget(task.GetType().Name);
        }

        Log.Information("MaintenanceService: Started background tasks");
    }

    private async Task RunTaskLoopAsync(IMaintenanceTask task, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var combinedCt = linkedCts.Token;

        try
        {
            if (task.InitialDelay > TimeSpan.Zero)
            {
                await Task.Delay(task.InitialDelay, combinedCt);
            }

            while (!combinedCt.IsCancellationRequested)
            {
                try
                {
                    await task.ExecuteAsync(combinedCt);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "MaintenanceService: Error executing {TaskName}", task.GetType().Name);
                }

                await Task.Delay(task.Interval, combinedCt);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful exit on cancellation
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

