using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Services;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Maintenance;

public class DatabaseMaintenanceTask : IMaintenanceTask
{
    private readonly DatabaseMaintenance _dbMaintenance;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly ImageCacheService _imageCacheService;

    public DatabaseMaintenanceTask(
        DatabaseMaintenance dbMaintenance,
        IUserAnimeRepository userAnimeRepo,
        ImageCacheService imageCacheService)
    {
        _dbMaintenance = dbMaintenance;
        _userAnimeRepo = userAnimeRepo;
        _imageCacheService = imageCacheService;
    }

    public TimeSpan InitialDelay => TimeSpan.FromHours(1);
    public TimeSpan Interval => TimeSpan.FromDays(1);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        Log.Information("MaintenanceTask: Triggering Database and Image Cache maintenance...");
        
        // Step 1: Clean Database (History, Orphaned Metadata, Episode Releases, Stuck Tasks)
        await _dbMaintenance.PerformAsync();

        // Step 2: Get active image paths from DB
        var activePaths = await _userAnimeRepo.GetActiveLocalImagePathsAsync();
        
        // Step 3: Perform smart cleanup of the image folder (delete files NOT in activePaths)
        await _imageCacheService.PerformSmartCleanupAsync(activePaths);
    }
}
