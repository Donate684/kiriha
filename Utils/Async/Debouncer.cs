using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kiriha.Utils.Async;

/// <summary>
/// Delays execution of an action until a specified time has passed without further invocations.
/// </summary>
public class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<CancellationToken, Task> _action;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private bool _isDisposed;

    public Debouncer(TimeSpan delay, Func<CancellationToken, Task> action)
    {
        _delay = delay;
        _action = action;
    }

    public Debouncer(TimeSpan delay, Action action)
    {
        _delay = delay;
        _action = _ => { action(); return Task.CompletedTask; };
    }

    public void Invoke()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            
            var token = _cts.Token;
            ExecuteAsync(token).SafeFireAndForget("Debouncer.Invoke");
        }
    }

    public void CancelPending()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token).ConfigureAwait(false);
            await _action(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* Expected */ }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}

/// <summary>
/// A generic debouncer that allows passing a value to the debounced action.
/// </summary>
public class Debouncer<T> : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<T, CancellationToken, Task> _action;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();
    private bool _isDisposed;

    public Debouncer(TimeSpan delay, Func<T, CancellationToken, Task> action)
    {
        _delay = delay;
        _action = action;
    }

    public void Invoke(T value)
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            var token = _cts.Token;
            ExecuteAsync(value, token).SafeFireAndForget($"Debouncer<{typeof(T).Name}>.Invoke");
        }
    }

    private async Task ExecuteAsync(T value, CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token).ConfigureAwait(false);
            await _action(value, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* Expected */ }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
