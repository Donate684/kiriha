using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Services.Tracking;
using Serilog;

namespace Kiriha.Services.Maintenance;

public class AiringSyncMaintenanceTask : IMaintenanceTask
{
    private readonly AiringInfoService _airingService;

    public AiringSyncMaintenanceTask(AiringInfoService airingService)
    {
        _airingService = airingService;
    }

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(5);
    public TimeSpan Interval => TimeSpan.FromHours(6);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        Log.Debug("MaintenanceTask: Triggering Airing Info sync...");
        await _airingService.SyncOngoingEpisodesAsync(false, null, ct);
    }
}
