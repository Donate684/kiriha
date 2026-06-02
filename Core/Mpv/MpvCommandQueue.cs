using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Mpv;

internal sealed class MpvCommandQueue
{
    private readonly object _gate;
    private readonly object _pendingGate = new();
    private readonly Func<IntPtr> _getHandle;
    private readonly Func<bool> _isDisposed;
    private readonly Dictionary<string, long> _latestCoalescedVersions = new(StringComparer.Ordinal);
    private readonly Channel<QueuedMpvCommand> _queue = Channel.CreateUnbounded<QueuedMpvCommand>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private Task? _loopTask;
    private long _nextCoalescedVersion;

    public MpvCommandQueue(object gate, Func<IntPtr> getHandle, Func<bool> isDisposed)
    {
        _gate = gate;
        _getHandle = getHandle;
        _isDisposed = isDisposed;
    }

    public void Start()
    {
        _loopTask = Task.Run(CommandLoopAsync);
    }

    public bool Enqueue(Action<IntPtr> action, string? coalescingKey = null)
    {
        lock (_gate)
        {
            if (_isDisposed() || _getHandle() == IntPtr.Zero)
                return false;
        }

        var command = CreateCommand(action, coalescingKey);
        if (_queue.Writer.TryWrite(command))
            return true;

        Log.Debug("Ignored mpv command after command queue completed");
        return false;
    }

    public void Complete()
    {
        _queue.Writer.TryComplete();
    }

    public bool WaitForStop(TimeSpan timeout)
    {
        return MpvTask.Wait(_loopTask, timeout, "mpv command loop");
    }

    private async Task CommandLoopAsync()
    {
        await foreach (var command in _queue.Reader.ReadAllAsync())
        {
            if (IsSuperseded(command))
                continue;

            try
            {
                lock (_gate)
                {
                    if (_isDisposed() || _getHandle() == IntPtr.Zero)
                        continue;

                    command.Execute(_getHandle());
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MpvPlayer command failed");
            }
        }
    }

    private QueuedMpvCommand CreateCommand(Action<IntPtr> action, string? coalescingKey)
    {
        if (string.IsNullOrWhiteSpace(coalescingKey))
            return new QueuedMpvCommand(action, null, 0);

        lock (_pendingGate)
        {
            var version = ++_nextCoalescedVersion;
            _latestCoalescedVersions[coalescingKey] = version;
            return new QueuedMpvCommand(action, coalescingKey, version);
        }
    }

    private bool IsSuperseded(QueuedMpvCommand command)
    {
        if (command.CoalescingKey == null)
            return false;

        lock (_pendingGate)
        {
            return _latestCoalescedVersions.TryGetValue(command.CoalescingKey, out var latestVersion) &&
                   latestVersion != command.CoalescingVersion;
        }
    }

    private sealed record QueuedMpvCommand(Action<IntPtr> Execute, string? CoalescingKey, long CoalescingVersion);
}
