using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Player;
using Kiriha.Models.Api;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.Tracking;

public class InternalPlayerServer : BackgroundService
{
    private readonly TrackingService _trackingService;
    private readonly object _pipeGate = new();
    private NamedPipeServerStream? _currentPipe;

    public InternalPlayerServer(TrackingService trackingService)
    {
        _trackingService = trackingService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    InternalPlayerBridge.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                lock (_pipeGate) _currentPipe = pipeServer;

                Log.Debug("InternalPlayerServer: Waiting for player connection...");
                await pipeServer.WaitForConnectionAsync(stoppingToken);
                Log.Information("InternalPlayerServer: Player connected.");

                using var reader = new System.IO.StreamReader(pipeServer);

                while (pipeServer.IsConnected && !stoppingToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(stoppingToken);
                    if (string.IsNullOrEmpty(line))
                    {
                        // Pipe closed or empty
                        break;
                    }

                    try
                    {
                        var state = JsonSerializer.Deserialize<InternalPlayerState>(line);
                        if (state != null)
                        {
                            _trackingService.SetInternalMedia(state);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "InternalPlayerServer: Failed to parse IPC message");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
                break;
            }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "InternalPlayerServer: Error in pipe loop");
                await Task.Delay(1000, stoppingToken);
            }
            finally
            {
                lock (_pipeGate)
                {
                    if (ReferenceEquals(_currentPipe, pipeServer))
                        _currentPipe = null;
                }
                pipeServer?.Dispose();

                // Whenever pipe breaks or player disconnects, clear the media
                Log.Information("InternalPlayerServer: Player disconnected. Clearing media.");
                _trackingService.SetInternalMedia(new InternalPlayerState { IsClosed = true });
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        var stopTask = base.StopAsync(cancellationToken);

        lock (_pipeGate)
        {
            _currentPipe?.Dispose();
            _currentPipe = null;
        }

        return stopTask;
    }
}
