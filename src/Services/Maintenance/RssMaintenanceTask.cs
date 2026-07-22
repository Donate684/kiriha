using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Services.Tracking;
using Serilog;

namespace Kiriha.Services.Maintenance;

public class RssMaintenanceTask : IMaintenanceTask
{
    private readonly RssFeedService _rssService;

    public RssMaintenanceTask(RssFeedService rssService)
    {
        _rssService = rssService;
    }

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(5);
    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        Log.Debug("MaintenanceTask: Triggering RSS check...");
        await _rssService.CheckFeedsAsync();
    }
}
