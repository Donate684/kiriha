using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models.Api;

namespace Kiriha.Core;

public sealed class InternalPlayerStateClient : IDisposable
{
    private readonly object _connectionGate = new();
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private NamedPipeClientStream? _client;
    private StreamWriter? _writer;
    private InternalPlayerState? _pendingState;
    private bool _disposed;

    public async Task ConnectAsync(int timeoutMs = 1000)
    {
        if (_disposed) return;

        try
        {
            var client = new NamedPipeClientStream(
                ".",
                InternalPlayerBridge.PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(timeoutMs);
            _client = client;
            _writer = new StreamWriter(client) { AutoFlush = true };

            InternalPlayerState? pendingState;
            lock (_stateGate)
            {
                pendingState = _pendingState;
                _pendingState = null;
            }

            if (pendingState != null)
                await WriteAsync(pendingState);
        }
        catch
        {
            DisposeConnection();
        }
    }

    public void Publish(InternalPlayerState state)
    {
        if (_disposed || _writer == null || _client?.IsConnected != true)
        {
            lock (_stateGate)
            {
                if (!_disposed)
                    _pendingState = state;
            }
            return;
        }

        _ = WriteAsync(state);
    }

    public void PublishClosed()
    {
        if (_writer == null || _client?.IsConnected != true)
            return;

        try
        {
            _writer.WriteLine(JsonSerializer.Serialize(new InternalPlayerState { IsClosed = true }));
        }
        catch
        {
        }
    }

    private async Task WriteAsync(InternalPlayerState state)
    {
        await _writeGate.WaitAsync();
        try
        {
            if (_disposed || _writer == null || _client?.IsConnected != true)
                return;

            await _writer.WriteLineAsync(JsonSerializer.Serialize(state));
        }
        catch
        {
            DisposeConnection();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            _disposed = true;
            _pendingState = null;
        }

        DisposeConnection();
    }

    private void DisposeConnection()
    {
        StreamWriter? writer;
        NamedPipeClientStream? client;

        lock (_connectionGate)
        {
            writer = _writer;
            client = _client;
            _writer = null;
            _client = null;
        }

        DisposeIgnoringBrokenPipe(writer);
        DisposeIgnoringBrokenPipe(client);
    }

    private static void DisposeIgnoringBrokenPipe(IDisposable? disposable)
    {
        if (disposable == null)
            return;

        try
        {
            disposable.Dispose();
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
