using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Shiki;
using Kiriha.Services;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Maintenance;

public class MetadataFetchMaintenanceTask : IMaintenanceTask
{
    private readonly SettingsService _settingsService;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly IMetadataRepository _metadataRepo;
    private readonly ShikiMetadataService _shikiMetadata;
    private readonly ImageCacheService _imageCacheService;
    private readonly IUiDispatcher _uiDispatcher;

    public MetadataFetchMaintenanceTask(
        SettingsService settingsService,
        IUserAnimeRepository userAnimeRepo,
        IMetadataRepository metadataRepo,
        ShikiMetadataService shikiMetadata,
        ImageCacheService imageCacheService,
        IUiDispatcher uiDispatcher)
    {
        _settingsService = settingsService;
        _userAnimeRepo = userAnimeRepo;
        _metadataRepo = metadataRepo;
        _shikiMetadata = shikiMetadata;
        _imageCacheService = imageCacheService;
        _uiDispatcher = uiDispatcher;
    }

    public TimeSpan InitialDelay => TimeSpan.FromMinutes(2);
    public TimeSpan Interval => _settingsService.Current.System.EnableBackgroundMetadataFetch ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(5);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
            return;

        // Scan the entire database
        var allItems = await _userAnimeRepo.GetAllAsync();
        var existingIds = await _metadataRepo.GetAllIdsAsync();

        var missingItems = new System.Collections.Generic.List<Kiriha.Models.AnimeItem>();

        foreach (var item in allItems)
        {
            int cacheId = item.MediaKind switch
            {
                Kiriha.Models.Entities.MediaKind.Manga => item.Id | 0x40000000,
                Kiriha.Models.Entities.MediaKind.LightNovel => item.Id | 0x20000000,
                _ => item.Id
            };

            if (!existingIds.Contains(cacheId))
            {
                missingItems.Add(item);
            }
        }

        if (missingItems.Count > 0)
        {
            Log.Information("MetadataFetcherTask: Found {Count} items missing metadata.", missingItems.Count);

            foreach (var missing in missingItems)
            {
                ct.ThrowIfCancellationRequested();

                if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
                    break;

                // Wait until window is minimized before fetching
                await WaitUntilMinimizedAsync(ct);

                if (!_settingsService.Current.System.EnableBackgroundMetadataFetch)
                    break;

                try
                {
                    // Fetch metadata one by one
                    var fetched = await _shikiMetadata.GetOrFetchMetadataAsync(missing.Id, null, null, missing.MediaKind);
                    
                    // Ensure main poster is downloaded
                    if (!string.IsNullOrEmpty(missing.MainPictureUrl))
                    {
                        await _imageCacheService.GetLocalPathOrDownload(missing.MainPictureUrl);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "MetadataFetcherTask: Failed to fetch metadata or poster for Item {Id}", missing.Id);
                }

                // We can't fetch Shiki Relations because they are not part of ShikiMetadata, 
                // they are fetched via JikanApiService. If the user wants relations, they are usually 
                // fetched lazily on the Details page. We will just delay.
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }

    private async Task WaitUntilMinimizedAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs>? propertyHandler = null;
        EventHandler? closedHandler = null;
        Avalonia.Controls.Window? mainWindow = null;

        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    mainWindow = desktop.MainWindow;
                    if (mainWindow == null || !mainWindow.IsVisible || mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    {
                        tcs.TrySetResult(true);
                        return;
                    }

                    propertyHandler = (_, args) =>
                    {
                        if (args.Property == Avalonia.Visual.IsVisibleProperty || args.Property == Avalonia.Controls.Window.WindowStateProperty)
                        {
                            if (!mainWindow.IsVisible || mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                            {
                                tcs.TrySetResult(true);
                            }
                        }
                    };

                    closedHandler = (_, __) =>
                    {
                        tcs.TrySetResult(true);
                    };

                    mainWindow.PropertyChanged += propertyHandler;
                    mainWindow.Closed += closedHandler;

                    if (!mainWindow.IsVisible || mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
                    {
                        tcs.TrySetResult(true);
                    }
                }
                else
                {
                    tcs.TrySetResult(true);
                }
            });

            await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            await tcs.Task;
        }
        finally
        {
            _uiDispatcher.Post(() =>
            {
                if (mainWindow != null)
                {
                    if (propertyHandler != null) mainWindow.PropertyChanged -= propertyHandler;
                    if (closedHandler != null) mainWindow.Closed -= closedHandler;
                }
            });
        }
    }
}
