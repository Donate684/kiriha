using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking.Anisthesia;
using Serilog;

namespace Kiriha.Services.Tracking;

public class AnisthesiaService : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly SmtcService _smtcService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly DetectionManager _detectionManager;
    private readonly PauseDetector _pauseDetector = new();
    private readonly List<AnisthesiaPlayer> _availablePlayers;
    private HashSet<string> _runningPlayerNames = new();
    private uint _lastTrackedPid;
    private readonly CancellationTokenSource _disposeCts = new();

    public event EventHandler<ParsedMedia>? MediaDetected;
    public event EventHandler? MediaCleared;
    public event EventHandler<HashSet<string>>? RunningPlayersChanged;

    public List<AnisthesiaPlayer> AvailablePlayers => _availablePlayers;
    public HashSet<string> RunningPlayerNames => _runningPlayerNames;

    public AnisthesiaService(
        SettingsService settingsService,
        SmtcService smtcService,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _settingsService = settingsService;
        _smtcService = smtcService;
        _backgroundTasks = backgroundTasks;
        
        // Load players from resources
        try
        {
            var uri = new Uri("avares://Kiriha/Assets/Anisthesia/players.anisthesia");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var data = reader.ReadToEnd();
            _availablePlayers = PlayerParser.ParseData(data);
            Log.Information("Loaded {Count} players from Anisthesia embedded data.", _availablePlayers.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnisthesiaService: Failed to load embedded players data. Fallback to empty list.");
            _availablePlayers = new List<AnisthesiaPlayer>();
        }

        _detectionManager = new DetectionManager(_availablePlayers, _settingsService);

        _backgroundTasks.Run("AnisthesiaService.PollingLoop", async ct =>
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
                await PollingLoopAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        });
    }

    private async Task PollingLoopAsync(CancellationToken ct)
    {
        Log.Information("Anisthesia Polling Service started.");
        
        await _smtcService.StartAsync();
        
        ParsedMedia? lastDetected = null;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Update running players set
                var running = _detectionManager.GetRunningPlayerNames();
                
                if (!running.SetEquals(_runningPlayerNames))
                {
                    _runningPlayerNames = running;
                    RunningPlayersChanged?.Invoke(this, _runningPlayerNames);
                }

                var detected = await _detectionManager.DetectAsync();
                
                if (detected != null)
                {
                    var timeline = _smtcService.GetTimeline(detected.ProcessName);
                    if (timeline.HasValue)
                    {
                        detected.Position = timeline.Value.Position;
                        detected.Duration = timeline.Value.Duration;
                    }

                    // Reconcile the strategy's optimistic IsPlaying=true against
                    // the audio session state. WindowTitleStrategy and HandleEnumerationStrategy
                    // can't tell pause from play on their own — the player process
                    // and the open file handle look identical in both cases. The
                    // audio session, however, goes Inactive within ~1 s of pause
                    // for every mainstream player, so we treat that as the
                    // authoritative signal (debounced inside PauseDetector to
                    // tolerate quiet scenes / silent intros).
                    if (detected.Pid != 0)
                    {
                        if (_lastTrackedPid != 0 && _lastTrackedPid != detected.Pid)
                        {
                            // The user switched to a different player instance —
                            // drop the previous tracker so we don't carry over
                            // its Inactive streak.
                            _pauseDetector.Forget(_lastTrackedPid);
                        }
                        _lastTrackedPid = detected.Pid;

                        var audioState = AudioSessionProbe.GetStateForPid(detected.Pid);
                        var contextLabel = string.IsNullOrEmpty(detected.Episode)
                            ? detected.AnimeTitle
                            : $"{detected.AnimeTitle} ep {detected.Episode}";
                        detected.IsPlaying = _pauseDetector.Update(detected.Pid, audioState, contextLabel);
                    }

                    // Update only if title or episode or playing state changed
                    if (lastDetected == null || 
                        lastDetected.AnimeTitle != detected.AnimeTitle || 
                        lastDetected.Episode != detected.Episode || 
                        lastDetected.IsPlaying != detected.IsPlaying)
                    {
                        lastDetected = detected;
                        MediaDetected?.Invoke(this, detected);
                    }
                }
                else if (lastDetected != null)
                {
                    if (_lastTrackedPid != 0)
                    {
                        _pauseDetector.Forget(_lastTrackedPid);
                        _lastTrackedPid = 0;
                    }
                    lastDetected = null;
                    MediaCleared?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Anisthesia polling error: {Msg}", ex.Message);
            }

            await Task.Delay(5000, ct); // Poll every 5s
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_disposeCts.IsCancellationRequested)
            {
                _disposeCts.Cancel();
            }
        }
        catch (ObjectDisposedException) { }
        
        _disposeCts.Dispose();

        MediaDetected = null;
        MediaCleared = null;
        RunningPlayersChanged = null;
    }
}
