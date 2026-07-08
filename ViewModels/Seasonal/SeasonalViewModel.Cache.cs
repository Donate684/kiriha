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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using Serilog;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    private void HydrateDiskCacheOnce()
    {
        if (Interlocked.CompareExchange(ref _diskHydrated, 1, 0) != 0) return;

        try
        {
            foreach (var entry in _cacheStore.LoadAll())
            {
                _seasonalCache.TryAdd((entry.Year, entry.Season), entry.Items);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SeasonalViewModel: disk cache hydration failed");
        }
    }

    private void ScheduleDeferredInitialLoad()
    {
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (_isDisposed) return;
                Dispatcher.UIThread.Post(() => EnsureInitialLoad());
            }
            catch
            {
                // fire-and-forget
            }
        });
    }

    public void EnsureInitialLoad()
    {
        if (_isDisposed) return;
        if (Interlocked.CompareExchange(ref _initialLoadStarted, 1, 0) != 0) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    public void InvalidateCache()
    {
        _seasonalCache.Clear();
        _allSeasonalItems = new List<AnimeItem>();
        DisplayItems = new AvaloniaList<AnimeItem>();

        if (!_isDisposed && Volatile.Read(ref _initialLoadStarted) != 0)
            LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    [RelayCommand]
    public async Task LoadSeasonalAnimeAsync()
    {
        if (_isDisposed) return;

        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _loadCts, newCts);

        if (oldCts != null)
        {
            try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            oldCts.Dispose();
        }

        var ct = newCts.Token;
        var capturedYear = CurrentYear;
        var capturedSeason = CurrentSeason;
        var cacheKey = (capturedYear, capturedSeason);
        bool hasCache = _seasonalCache.TryGetValue(cacheKey, out var cached);

        if (!hasCache) IsLoading = true;

        try
        {
            if (hasCache)
            {
                _allSeasonalItems = cached!;
                await ApplyFiltersAsync();
                _ = RefreshSeasonalCacheInBackground(capturedYear, capturedSeason, ct);
            }
            else
            {
                _allSeasonalItems = new List<AnimeItem>();
                var fresh = await _apiService.GetSeasonalAnimeAsync(capturedYear, capturedSeason, ct);
                if (ct.IsCancellationRequested) return;
                if (fresh != null && fresh.Any())
                {
                    SaveSeasonalCache(capturedYear, capturedSeason, fresh);
                    _allSeasonalItems = fresh;
                }
                await ApplyFiltersAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load seasonal anime");
        }
        finally
        {
            if (Volatile.Read(ref _loadCts) == newCts)
            {
                IsLoading = false;
            }
        }
    }

    private Task RefreshSeasonalCacheInBackground(int year, string season, CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                var fresh = await _apiService.GetSeasonalAnimeAsync(year, season, ct);
                if (ct.IsCancellationRequested) return;
                if (fresh == null || !fresh.Any()) return;

                SaveSeasonalCache(year, season, fresh);
                if (year == CurrentYear && season == CurrentSeason)
                {
                    _allSeasonalItems = fresh;
                    await ApplyFiltersAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Debug(ex, "Background seasonal refresh failed");
            }
        }, ct);

    private void SaveSeasonalCache(int year, string season, List<AnimeItem> items)
    {
        _seasonalCache[(year, season)] = items;
        _ = _cacheStore.SaveAsync(year, season, items);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var cts = Interlocked.Exchange(ref _loadCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        _filterDebouncer?.Dispose();
        _applyFilterDebouncer?.Dispose();
    }
}
