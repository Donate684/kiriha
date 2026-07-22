using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed class AppReadinessService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Task? _startupTask;
    private AppReadinessState _state = AppReadinessState.NotStarted;

    public AppReadinessService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public event EventHandler<AppReadinessState>? StateChanged;

    public AppReadinessState State
    {
        get
        {
            lock (_gate) return _state;
        }
    }

    public Task ReadyTask => _readyTcs.Task;

    public Task StartAsync()
    {
        lock (_gate)
        {
            _startupTask ??= StartCoreAsync();
            return _startupTask;
        }
    }

    private async Task StartCoreAsync()
    {
        SetState(AppReadinessState.Starting);
        var total = Stopwatch.StartNew();

        try
        {
            var stage = Stopwatch.StartNew();
            var databaseInitializer = _serviceProvider.GetRequiredService<DatabaseInitializer>();
            await databaseInitializer.InitializeAsync();
            await databaseInitializer.InitializationTask;
            Log.Information("StartupTiming: readiness database stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            var animeRepo = _serviceProvider.GetRequiredService<AnimeRepository>();
            await animeRepo.InitializeAsync();
            await animeRepo.InitializationTask;
            
            if (animeRepo.Collection.Count == 0)
            {
                var orchestrator = _serviceProvider.GetRequiredService<AnimeSyncOrchestrator>();
                await orchestrator.SyncWithTrackersAsync();
            }
            Log.Information("StartupTiming: readiness anime stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            _serviceProvider.GetRequiredService<NotificationService>();
            _serviceProvider.GetRequiredService<DiscordService>().Initialize();
            await _serviceProvider.GetRequiredService<SmtcService>().StartAsync();
            _serviceProvider.GetRequiredService<MaintenanceService>().Start();
            Log.Information("StartupTiming: readiness foreground services stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            foreach (var hosted in _serviceProvider.GetServices<IHostedService>())
            {
                try
                {
                    await hosted.StartAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to start hosted service {Type}", hosted.GetType().Name);
                }
            }
            Log.Information("StartupTiming: readiness hosted services stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            if (_serviceProvider.GetRequiredService<SettingsService>().Current.System.KeepPlayerProcessAlive)
                PlayerProcessBridge.StartResident();
            Log.Information("StartupTiming: readiness resident player stage elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            SetState(AppReadinessState.Ready);
            _readyTcs.TrySetResult();
            Log.Information("StartupTiming: readiness complete elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "App readiness failed during startup");
            SetState(AppReadinessState.Failed);
            _readyTcs.TrySetException(ex);
        }
    }

    private void SetState(AppReadinessState state)
    {
        lock (_gate)
        {
            if (_state == state) return;
            _state = state;
        }

        StateChanged?.Invoke(this, state);
    }
}
