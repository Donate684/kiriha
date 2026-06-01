using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Mpv;

internal sealed class MpvCommandQueue
{
    private readonly object _gate;
    private readonly Func<IntPtr> _getHandle;
    private readonly Func<bool> _isDisposed;
    private readonly Channel<Action<IntPtr>> _queue = Channel.CreateUnbounded<Action<IntPtr>>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private Task? _loopTask;

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

    public bool Enqueue(Action<IntPtr> action)
    {
        lock (_gate)
        {
            if (_isDisposed() || _getHandle() == IntPtr.Zero)
                return false;
        }

        if (_queue.Writer.TryWrite(action))
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
            try
            {
                lock (_gate)
                {
                    if (_isDisposed() || _getHandle() == IntPtr.Zero)
                        continue;

                    command(_getHandle());
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MpvPlayer command failed");
            }
        }
    }
}
