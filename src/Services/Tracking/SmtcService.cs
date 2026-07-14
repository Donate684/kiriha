using System;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Services.Data;
using Serilog;
using Windows.Media.Control;

namespace Kiriha.Services.Tracking;

public class SmtcService : IDisposable
{
    private readonly SettingsService _settingsService;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public bool DiscoveryMode { get; set; }

    public SmtcService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task StartAsync()
    {
        try
        {
            if (_manager == null && OperatingSystem.IsWindows())
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                Log.Information("SMTC Session Manager initialized.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize SMTC Session Manager");
        }
    }

    public (TimeSpan Position, TimeSpan Duration, DateTimeOffset LastUpdatedTime)? GetTimeline(string processName)
    {
        if (_manager == null || !OperatingSystem.IsWindows()) return null;

        try
        {
            var sessions = _manager.GetSessions();
            var session = sessions.FirstOrDefault(s => s.SourceAppUserModelId.Contains(processName, StringComparison.OrdinalIgnoreCase))
                          ?? _manager.GetCurrentSession();

            if (session != null)
            {
                var timeline = session.GetTimelineProperties();
                if (timeline != null && timeline.EndTime > TimeSpan.Zero)
                {
                    return (timeline.Position, timeline.EndTime - timeline.StartTime, timeline.LastUpdatedTime);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error reading SMTC timeline for {ProcessName}", processName);
        }

        return null;
    }

    public void RequestRefresh()
    {
    }

    public void Dispose()
    {
    }
}
