using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Serilog;

namespace Kiriha.ViewModels;

public partial class SettingsViewModel
{
    [RelayCommand(CanExecute = nameof(CanRefreshCacheStats))]
    private async Task RefreshCacheStats()
    {
        IsCacheBusy = true;
        try
        {
            var stats = await _cacheCleanupService.GetStatsAsync();
            foreach (var stat in stats)
            {
                var item = CacheItems.FirstOrDefault(x => x.Target == stat.Target);
                if (item == null) continue;
                item.ItemCount = stat.ItemCount;
                item.SizeBytes = stat.SizeBytes;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to refresh cache stats");
            CacheStatus = UIUtils.GetLoc("common.errors.generic");
        }
        finally
        {
            IsCacheBusy = false;
        }
    }

    private bool CanRefreshCacheStats() => !IsCacheBusy;

    [RelayCommand(CanExecute = nameof(CanClearSelectedCache))]
    private async Task ClearSelectedCache()
    {
        var selected = CacheItems.Where(x => x.IsSelected).Select(x => x.Target).ToList();
        if (selected.Count == 0) return;

        IsCacheBusy = true;
        CacheStatus = string.Empty;
        try
        {
            await _cacheCleanupService.ClearAsync(selected);
            InvalidateRuntimeCaches(selected);
            foreach (var item in CacheItems) item.IsSelected = false;
            CacheStatus = UIUtils.GetLoc("settings.cache.cleared");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear selected cache");
            CacheStatus = UIUtils.GetLoc("common.errors.generic");
        }
        finally
        {
            IsCacheBusy = false;
            await RefreshCacheStats();
        }
    }

    private void InvalidateRuntimeCaches(IReadOnlyCollection<CacheCleanupTarget> selected)
    {
        if (selected.Contains(CacheCleanupTarget.ImageFiles))
            _imageCacheService.ClearMemoryCache();

        if (selected.Contains(CacheCleanupTarget.RecognitionCache))
            _mappingService.ClearRecognitionCaches();

        if (selected.Contains(CacheCleanupTarget.SeasonalCache))
            _seasonalViewModel.InvalidateCache();
    }
}
