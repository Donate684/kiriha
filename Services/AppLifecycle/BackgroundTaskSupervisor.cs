using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed class BackgroundTaskSupervisor : IBackgroundTaskSupervisor, IDisposable
{
    private sealed class TrackedTask
    {
        public required string Name { get; init; }
        public required CancellationTokenSource Cancellation { get; init; }
        public Task? Task { get; set; }
    }

    private readonly ConcurrentDictionary<int, TrackedTask> _tasks = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private int _nextId;
    private int _stopped;

    public Task Run(string name, Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (Volatile.Read(ref _stopped) != 0)
        {
            Log.Warning("Background task {Name} was not started because shutdown is in progress", name);
            return Task.CompletedTask;
        }

        var id = Interlocked.Increment(ref _nextId);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token, cancellationToken);
        var tracked = new TrackedTask { Name = name, Cancellation = linkedCts };

        if (!_tasks.TryAdd(id, tracked))
        {
            linkedCts.Dispose();
            return Task.CompletedTask;
        }

        var task = Task.Run(async () =>
        {
            try
            {
                Log.Debug("Background task {Name} started", name);
                await operation(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                Log.Debug("Background task {Name} cancelled", name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Background task {Name} failed", name);
            }
            finally
            {
                _tasks.TryRemove(id, out _);
                linkedCts.Dispose();
                Log.Debug("Background task {Name} finished", name);
            }
        }, CancellationToken.None);

        tracked.Task = task;

        return task;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        _shutdownCts.Cancel();

        var running = _tasks.Values.ToArray();
        if (running.Length == 0)
            return;

        Log.Information(
            "Stopping {Count} background task(s): {Tasks}",
            running.Length,
            string.Join(", ", running.Select(t => t.Name).Distinct()));

        try
        {
            await Task.WhenAll(running.Select(t => t.Task).OfType<Task>()).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var stillRunning = _tasks.Values.Select(t => t.Name).Distinct().ToArray();
            if (stillRunning.Length > 0)
                Log.Warning("Background task shutdown timed out: {Tasks}", string.Join(", ", stillRunning));
        }
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
    }
}
