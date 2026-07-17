using System;
using System.Linq;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Services.Data;
using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;
using Kiriha.Views;
using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services;

public class InstanceServer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly object _pipeGate = new();
    private NamedPipeServerStream? _currentPipe;

    public InstanceServer(IServiceProvider serviceProvider, IUiDispatcher uiDispatcher)
    {
        _serviceProvider = serviceProvider;
        _uiDispatcher = uiDispatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    "Kiriha.InstanceServer",
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                lock (_pipeGate) _currentPipe = pipeServer;

                await pipeServer.WaitForConnectionAsync(stoppingToken);

                using var reader = new System.IO.StreamReader(pipeServer);
                var line = await reader.ReadLineAsync(stoppingToken);

                if (!string.IsNullOrEmpty(line))
                {
                    var args = PipeArgumentSerializer.Deserialize(line);
                    HandleArguments(args);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Error(ex, "InstanceServer error");
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

    private void HandleArguments(string[] args)
    {
        int playerArgIndex = Array.FindIndex(args, arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));
        
        if (playerArgIndex >= 0)
        {
            string videoUrl = string.Empty;
            if (playerArgIndex + 1 < args.Length && !args[playerArgIndex + 1].StartsWith("--"))
            {
                videoUrl = args[playerArgIndex + 1];
            }

            _uiDispatcher.Post(() => {
                var metadataResolver = _serviceProvider.GetRequiredService<IPlayerMediaMetadataResolver>();
                var settingsService = _serviceProvider.GetService<SettingsService>();
                if (settingsService?.Current.Player.SingleWindow == true
                    && Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var existingWindow = desktop.Windows.OfType<PlayerWindow>().LastOrDefault();
                    if (existingWindow?.DataContext is PlayerViewModel existingVm)
                    {
                        existingVm.LoadVideo(videoUrl);
                        existingWindow.Show();
                        existingWindow.Activate();
                        return;
                    }
                }

                var playerVm = new PlayerViewModel(videoUrl, metadataResolver.Resolve(videoUrl), metadataResolver, settingsService);
                var window = new PlayerWindow(settingsService!) { DataContext = playerVm };
                window.Show();
            });
        }
        else
        {
            _uiDispatcher.Post(() => {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    if (desktop.MainWindow != null)
                    {
                        desktop.MainWindow.ShowInTaskbar = true;
                        desktop.MainWindow.Show();
                        desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                        desktop.MainWindow.Activate();
                    }
                }
            });
        }
    }

}
