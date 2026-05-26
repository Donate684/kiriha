using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.ViewModels;

public partial class SeasonalViewModel : ViewModelBase, IDisposable
{
    private readonly MalApiService _apiService;
    private readonly SettingsService _settingsService;
    private readonly LoadQueueService _queueService;
    private readonly AnimeService _animeService;
    private readonly SeasonalCacheStore _cacheStore;
    private List<AnimeItem> _allSeasonalItems = new();
    private Dictionary<int, UserAnimeStatus> _userAnimeStore = new();
    private HashSet<int> _hiddenSeasonalIds = new();
    private static readonly ConcurrentDictionary<(int, string), List<AnimeItem>> _seasonalCache = new();
    private static int _diskHydrated; // 0 = not yet, 1 = done. Idempotent first-touch.
    private bool _isInitializing = true;

    private CancellationTokenSource? _loadCts;
    private bool _isDisposed;
    private readonly Utils.Debouncer _filterDebouncer;
    private readonly Utils.Debouncer _applyFilterDebouncer;
    private int _applyFiltersRequestCount = 0;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySeason))]
    private int _currentYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySeason))]
    private string _currentSeason;
    
    public string DisplaySeason => UIUtils.GetLoc("anime.seasons." + CurrentSeason.ToLower());
    
    [ObservableProperty] private AvaloniaList<AnimeItem> _displayItems = new();
    [ObservableProperty] private string _currentHeader = "";

    // Sorting
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySortBy))]
    private string _sortBy;
    public string DisplaySortBy => UIUtils.GetLoc("filters.sort." + SortBy.ToLower());
    
    public List<string> SortOptions { get; } = new() { Constants.Sorting.Popularity, Constants.Sorting.Score, Constants.Sorting.Title, Constants.Sorting.RussianTitle, Constants.Sorting.Date };

    // Search
    [ObservableProperty] private string? _searchQuery;
    partial void OnSearchQueryChanged(string? value) => ApplyFilters();

    // Categories
    [ObservableProperty] 
    [NotifyPropertyChangedFor(nameof(IsNewSelected))]
    [NotifyPropertyChangedFor(nameof(IsContinuingSelected))]
    [NotifyPropertyChangedFor(nameof(IsMoviesSelected))]
    [NotifyPropertyChangedFor(nameof(IsOnaSelected))]
    [NotifyPropertyChangedFor(nameof(IsOvaSelected))]
    [NotifyPropertyChangedFor(nameof(IsSpecialsSelected))]
    [NotifyPropertyChangedFor(nameof(IsOtherSelected))]
    private string _selectedCategory = "New";

    public bool IsNewSelected => SelectedCategory == "New";
    public bool IsContinuingSelected => SelectedCategory == "Continuing";
    public bool IsMoviesSelected => SelectedCategory == "Movies";
    public bool IsOnaSelected => SelectedCategory == "ONA";
    public bool IsOvaSelected => SelectedCategory == "OVA";
    public bool IsSpecialsSelected => SelectedCategory == "Specials";
    public bool IsOtherSelected => SelectedCategory == "Other";

    // Filters
    [ObservableProperty] private bool _filterNotInList;
    [ObservableProperty] private bool _filterWatching;
    [ObservableProperty] private bool _filterCompleted;
    [ObservableProperty] private bool _filterOnHold;
    [ObservableProperty] private bool _filterPlanToWatch;
    [ObservableProperty] private bool _filterDropped;
    [ObservableProperty] private bool _filterNsfw;
    [ObservableProperty] private bool _showHidden;

    [ObservableProperty] private bool _isFilterActive;
    
    [ObservableProperty] private string _newHeader = "";
    [ObservableProperty] private string _continuingHeader = "";
    [ObservableProperty] private string _moviesHeader = "";
    [ObservableProperty] private string _ovaHeader = "";
    [ObservableProperty] private string _onaHeader = "";
    [ObservableProperty] private string _specialsHeader = "";
    [ObservableProperty] private string _otherHeader = "";

    public bool UseFiveStarRating => _settingsService.Current.UI.UseFiveStarRating;

    public List<string> Seasons { get; } = new() { Constants.Seasons.Winter, Constants.Seasons.Spring, Constants.Seasons.Summer, Constants.Seasons.Fall };



    public SeasonalViewModel(MalApiService apiService, SettingsService settingsService, LoadQueueService queueService, AnimeService animeService, SeasonalCacheStore cacheStore)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _queueService = queueService;
        _animeService = animeService;
        _cacheStore = cacheStore;

        // Hydrate the in-memory _seasonalCache from disk exactly once per
        // process. Done synchronously here (sub-100 ms for typical sizes) so
        // that the very first LoadSeasonalAnimeAsync Ã¢â‚¬â€ whether triggered by
        // navigation or by the deferred prime Ã¢â‚¬â€ sees persisted data and can
        // serve a near-instant first paint.
        if (Interlocked.CompareExchange(ref _diskHydrated, 1, 0) == 0)
        {
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
        
        var settings = _settingsService.Current.UI;
        _sortBy = settings.SeasonalSortBy;
        _filterNsfw = settings.ShowNsfw;
        _showHidden = settings.SeasonalShowHidden;
        _hiddenSeasonalIds = new HashSet<int>(settings.HiddenSeasonalIds ?? new List<int>());
        _filterNotInList = settings.SeasonalStatusFilters.Contains("NotInList");
        _filterWatching = settings.SeasonalStatusFilters.Contains("Watching");
        _filterCompleted = settings.SeasonalStatusFilters.Contains("Completed");
        _filterOnHold = settings.SeasonalStatusFilters.Contains("OnHold");
        _filterPlanToWatch = settings.SeasonalStatusFilters.Contains("PlanToWatch");
        _filterDropped = settings.SeasonalStatusFilters.Contains("Dropped");

        int month = DateTime.Now.Month;
        _currentYear = DateTime.Now.Year;
        // December belongs to next year's Winter
        if (month == 12) _currentYear++;
        
        _currentSeason = month switch
        {
            1 or 2 or 12 => Constants.Seasons.Winter,
            3 or 4 or 5 => Constants.Seasons.Spring,
            6 or 7 or 8 => Constants.Seasons.Summer,
            _ => Constants.Seasons.Fall
        };

        _filterDebouncer = new Utils.Debouncer(TimeSpan.FromMilliseconds(500), async (ct) => {
            _settingsService.Update(settings =>
            {
                var s = settings.UI;
                s.SeasonalSortBy = SortBy;
                s.ShowNsfw = FilterNsfw;
                s.SeasonalShowHidden = ShowHidden;

                s.SeasonalStatusFilters.Clear();
                if (FilterNotInList) s.SeasonalStatusFilters.Add("NotInList");
                if (FilterWatching) s.SeasonalStatusFilters.Add("Watching");
                if (FilterCompleted) s.SeasonalStatusFilters.Add("Completed");
                if (FilterOnHold) s.SeasonalStatusFilters.Add("OnHold");
                if (FilterPlanToWatch) s.SeasonalStatusFilters.Add("PlanToWatch");
                if (FilterDropped) s.SeasonalStatusFilters.Add("Dropped");
            }, save: false);

            await _settingsService.SaveAsync();
        });

        _applyFilterDebouncer = new Utils.Debouncer(TimeSpan.FromMilliseconds(300), () => {
            ApplyFiltersAsync().SafeFireAndForget("ApplyFiltersAsync");
        });

        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) => {
            Dispatcher.UIThread.Post(() => {
                var vm = (SeasonalViewModel)r;
                var userStore = vm._animeService.Collection
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First().Status);
                vm.UpdateUserList(userStore);
            });
        });

        RefreshLocalization();
        _isInitializing = false;

        // Background prime: SeasonalVM is resolved as a singleton during
        // MainWindowViewModel construction (before the window is shown).
        // Calling LoadSeasonalAnimeAsync directly here used to make Shikimori
        // HTTP requests + metadata UI dispatches compete with the first few
        // render frames, causing visible jank on the Welcome view.
        //
        // 2-second delay is the sweet spot:
        //   * long enough that the first window paint + Mica composition + DB
        //     initial query all complete on the UI thread without contention,
        //   * short enough that by the time the user clicks "Seasonal" in the
        //     nav (typically 3+ seconds in), the network round-trip is
        //     already in flight or done.
        //
        // EnsureInitialLoad is idempotent, so the explicit
        // call from NavigateSeasonal will simply be a no-op if the prime
        // already fired, or it will start the load early if the user
        // navigates faster than the timer.
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (_isDisposed) return;
                Dispatcher.UIThread.Post(() => EnsureInitialLoad());
            }
            catch { /* fire-and-forget */ }
        });
    }

    private int _initialLoadStarted;

    /// <summary>
    /// Idempotent first-load trigger. Safe to call from navigation, from the
    /// deferred background prime, or from anywhere else - only the first call
    /// kicks off LoadSeasonalAnimeAsync.
    /// </summary>
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

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplaySeason));
        OnPropertyChanged(nameof(DisplaySortBy));
    }

    public void UpdateUserList(Dictionary<int, UserAnimeStatus> userList)
    {
        _userAnimeStore = userList;

        // Adding a title to the tracker list outranks the client-only
        // "not interested" flag: drop any hidden ids that now have a real
        // status so the item resurfaces in the default seasonal view (and
        // the hide button collapses back to its initial state).
        if (_hiddenSeasonalIds.Count > 0)
        {
            List<int>? toUnhide = null;
            foreach (var id in _hiddenSeasonalIds)
            {
                if (userList.TryGetValue(id, out var s) && s != UserAnimeStatus.None)
                    (toUnhide ??= new List<int>()).Add(id);
            }
            if (toUnhide != null)
            {
                foreach (var id in toUnhide)
                {
                    _hiddenSeasonalIds.Remove(id);
                }
                _settingsService.Update(settings =>
                {
                    foreach (var id in toUnhide)
                        settings.UI.HiddenSeasonalIds?.Remove(id);
                }, save: false);
                _ = _settingsService.SaveAsync();
            }
        }

        ApplyFilters();
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

        // Capture the season we're loading for, so that later async continuations
        // don't race with the user switching to another season.
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
                
                // Fetch fresh data in background to stay updated
                _ = Task.Run(async () => {
                    try {
                        var fresh = await _apiService.GetSeasonalAnimeAsync(capturedYear, capturedSeason, ct);
                        if (ct.IsCancellationRequested) return;
                        if (fresh != null && fresh.Any()) {
                            _seasonalCache[cacheKey] = fresh;
                            _ = _cacheStore.SaveAsync(capturedYear, capturedSeason, fresh);
                            // Only update current view if user is still on this season
                            if (capturedYear == CurrentYear && capturedSeason == CurrentSeason) {
                                _allSeasonalItems = fresh;
                                await ApplyFiltersAsync();
                            }
                        }
                    } catch (OperationCanceledException) { }
                    catch (Exception ex) { Log.Debug(ex, "Background seasonal refresh failed"); }
                }, ct);
            }
            else
            {
                // Clear stale items from previous season so UI doesn't show them
                // while the new season is loading.
                _allSeasonalItems = new List<AnimeItem>();

                var fresh = await _apiService.GetSeasonalAnimeAsync(capturedYear, capturedSeason, ct);
                if (ct.IsCancellationRequested) return;
                if (fresh != null && fresh.Any())
                {
                    _seasonalCache[cacheKey] = fresh;
                    _ = _cacheStore.SaveAsync(capturedYear, capturedSeason, fresh);
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
            // Always clear the loading overlay if this is still the latest load,
            // regardless of whether we were cancelled. Otherwise the overlay sticks
            // and the UI appears frozen.
            if (Volatile.Read(ref _loadCts) == newCts)
            {
                IsLoading = false;
            }
        }
    }

    public void ApplyFilters() => _applyFilterDebouncer.Invoke();

    private async Task ApplyFiltersAsync()
    {
        if (_isInitializing) return;

        int requestId = Interlocked.Increment(ref _applyFiltersRequestCount);
        _queueService.ClearQueues();

        var itemsToFilter = _allSeasonalItems ?? new List<AnimeItem>();
        var userStore = _userAnimeStore;
        var searchQuery = SearchQuery;
        var sortBy = SortBy;
        var filterNsfw = FilterNsfw;
        var selectedCategory = SelectedCategory;
        var currentYear = CurrentYear;
        var currentSeason = CurrentSeason;

        var (items, header, headers, resolvedCategory) = await Task.Run(() => 
        {
            IEnumerable<AnimeItem> query = itemsToFilter;

            // "Hidden" behaves like another status group: when its checkbox is on,
            // hidden ids are included (and become the only result if no other
            // status filter is set). When it's off, hidden ids are excluded.
            var hiddenIds = _hiddenSeasonalIds;
            bool wantHidden = ShowHidden;
            bool anyStatusFilter = FilterWatching || FilterCompleted || FilterOnHold || FilterPlanToWatch || FilterDropped;
            bool anyFilter = FilterNotInList || anyStatusFilter || wantHidden;

            if (!anyFilter)
            {
                // No filter checked: default view excludes hidden.
                if (hiddenIds.Count > 0)
                    query = query.Where(x => !hiddenIds.Contains(x.Id));
            }
            else
            {
                query = query.Where(x =>
                {
                    bool isHidden = hiddenIds.Count > 0 && hiddenIds.Contains(x.Id);
                    if (isHidden) return wantHidden;
                    return (FilterNotInList && x.Status == UserAnimeStatus.None) ||
                           (FilterWatching && x.Status == UserAnimeStatus.Watching) ||
                           (FilterCompleted && x.Status == UserAnimeStatus.Completed) ||
                           (FilterOnHold && x.Status == UserAnimeStatus.OnHold) ||
                           (FilterPlanToWatch && x.Status == UserAnimeStatus.PlanToWatch) ||
                           (FilterDropped && x.Status == UserAnimeStatus.Dropped);
                });
            }

            query = query.ApplySearch(searchQuery).ApplyNsfw(filterNsfw).ApplySorting(sortBy, isSeasonal: true);

            var allFiltered = query.ToList();
            var tvNew = new List<AnimeItem>();
            var tvContinuing = new List<AnimeItem>();
            var movies = new List<AnimeItem>();
            var ovas = new List<AnimeItem>();
            var onas = new List<AnimeItem>();
            var specials = new List<AnimeItem>();
            var others = new List<AnimeItem>();

            foreach (var item in allFiltered)
            {
                string t = (item.Type ?? "").ToLowerInvariant();

                if (t == Constants.AnimeTypes.Tv || t == Constants.AnimeTypes.TvSpecial)
                {
                    if (item.StartYear == currentYear && string.Equals(item.StartSeason, currentSeason, StringComparison.OrdinalIgnoreCase))
                        tvNew.Add(item);
                    else
                        tvContinuing.Add(item);
                }
                else if (t.Contains(Constants.AnimeTypes.Movie)) movies.Add(item);
                else if (t == Constants.AnimeTypes.Ova) ovas.Add(item);
                else if (t == Constants.AnimeTypes.Ona) onas.Add(item);
                else if (t == Constants.AnimeTypes.Special) specials.Add(item);
                else others.Add(item);
            }

            // If the user's selected category is empty but another one has items,
            // auto-switch to the nearest non-empty neighbour (right-first, then left)
            // so the user never ends up staring at a blank grid.
            var categoryCounts = new (string Key, int Count)[]
            {
                ("New", tvNew.Count),
                ("Continuing", tvContinuing.Count),
                ("Movies", movies.Count),
                ("ONA", onas.Count),
                ("OVA", ovas.Count),
                ("Specials", specials.Count),
                ("Other", others.Count)
            };
            int selectedIdx = Array.FindIndex(categoryCounts, c => c.Key == selectedCategory);
            if (selectedIdx < 0) selectedIdx = 0;
            if (categoryCounts[selectedIdx].Count == 0)
            {
                for (int dist = 1; dist < categoryCounts.Length; dist++)
                {
                    int right = selectedIdx + dist;
                    int left = selectedIdx - dist;
                    if (right < categoryCounts.Length && categoryCounts[right].Count > 0)
                    {
                        selectedCategory = categoryCounts[right].Key;
                        break;
                    }
                    if (left >= 0 && categoryCounts[left].Count > 0)
                    {
                        selectedCategory = categoryCounts[left].Key;
                        break;
                    }
                }
            }

            List<AnimeItem> resultItems = selectedCategory switch
            {
                "New" => tvNew,
                "Continuing" => tvContinuing,
                "Movies" => movies,
                "OVA" => ovas,
                "ONA" => onas,
                "Specials" => specials,
                _ => others
            };

            string GetHeader(string key, int count)
            {
                string loc = UIUtils.GetLoc(key);
                if (loc == key && key != "TV" && key != "OVA" && key != "ONA")
                    loc = char.ToUpper(loc[0]) + loc.Substring(1);
                return UIUtils.GetLoc("filters.header_format", loc, count.ToString());
            }

            string resultHeader = selectedCategory switch
            {
                "New" => GetHeader("anime.seasonal.categories.new", tvNew.Count),
                "Continuing" => GetHeader("anime.seasonal.categories.continuing", tvContinuing.Count),
                "Movies" => GetHeader("anime.seasonal.categories.movies", movies.Count),
                "OVA" => GetHeader("ova", ovas.Count),
                "ONA" => GetHeader("ona", onas.Count),
                "Specials" => GetHeader("anime.seasonal.categories.specials", specials.Count),
                _ => GetHeader("anime.seasonal.categories.other", others.Count)
            };

            var resultHeaders = new Dictionary<string, string>
            {
                ["New"] = GetHeader("anime.seasonal.categories.new", tvNew.Count),
                ["Continuing"] = GetHeader("anime.seasonal.categories.continuing", tvContinuing.Count),
                ["Movies"] = GetHeader("anime.seasonal.categories.movies", movies.Count),
                ["OVA"] = GetHeader("ova", ovas.Count),
                ["ONA"] = GetHeader("ona", onas.Count),
                ["Specials"] = GetHeader("anime.seasonal.categories.specials", specials.Count),
                ["Other"] = GetHeader("anime.seasonal.categories.other", others.Count)
            };

            return (resultItems.DistinctBy(x => x.Id).ToList(), resultHeader, resultHeaders, selectedCategory);
        });

        if (requestId != _applyFiltersRequestCount) return;

        await Dispatcher.UIThread.InvokeAsync(() => 
        {
            if (requestId != _applyFiltersRequestCount) return;

            // Update statuses on UI thread for the final set of items to avoid background thread notifications
            foreach (var item in items)
            {
                item.Status = userStore.TryGetValue(item.Id, out var status) ? status : UserAnimeStatus.None;
                item.IsHiddenInSeasons = _hiddenSeasonalIds.Contains(item.Id);
            }

            // Auto-switched category (happens when the user's selection was empty
            // and we fell back to a neighbour).
            if (SelectedCategory != resolvedCategory)
            {
                SelectedCategory = resolvedCategory;
            }

            // Skip replacing DisplayItems when the resulting list is identical
            // (same ids, same order). The background cache refresh runs
            // ApplyFiltersAsync a second time with often-unchanged data; blindly
            // reassigning DisplayItems would force ItemsRepeater to tear down and
            // rebuild every card, causing a visible "all-posters flicker" ~1s
            // after entering the view.
            bool unchanged = DisplayItems.Count == items.Count;
            if (unchanged)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (DisplayItems[i].Id != items[i].Id) { unchanged = false; break; }
                }
            }
            if (!unchanged)
            {
                DisplayItems = new AvaloniaList<AnimeItem>(items);
            }
            CurrentHeader = header;

            NewHeader = headers["New"];
            ContinuingHeader = headers["Continuing"];
            MoviesHeader = headers["Movies"];
            OvaHeader = headers["OVA"];
            OnaHeader = headers["ONA"];
            SpecialsHeader = headers["Specials"];
            OtherHeader = headers["Other"];

            IsFilterActive = FilterNotInList || FilterWatching || FilterCompleted || FilterOnHold || FilterPlanToWatch || FilterDropped || FilterNsfw || ShowHidden || !string.IsNullOrEmpty(SearchQuery);
            SaveSettingsDebounced();
        });
    }

    public void EnqueueItemForViewport(AnimeItem item)
    {
        if (item == null) return;
        _queueService.EnqueueForViewport(new[] { item });
    }

    [RelayCommand]
    public async Task SelectCategory(string category)
    {
        if (SelectedCategory == category) return;
        SelectedCategory = category;
        await ApplyFiltersAsync();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        FilterNotInList = false;
        FilterWatching = false;
        FilterCompleted = false;
        FilterOnHold = false;
        FilterPlanToWatch = false;
        FilterDropped = false;
        FilterNsfw = false;
        ShowHidden = false;
    }

    /// <summary>
    /// Client-only "not interested" toggle bound to the eye-off button on each
    /// seasonal card. Adds / removes the id from <c>AppSettings.UI.HiddenSeasonalIds</c>
    /// and persists immediately (the per-id list is too small to debounce).
    /// </summary>
    [RelayCommand]
    public void ToggleHiddenSeasonal(AnimeItem? item)
    {
        if (item == null) return;
        // Only titles that are NOT in the user's list can be hidden.
        // Already-hidden items can always be un-hidden, regardless of status.
        bool isHidden = _hiddenSeasonalIds.Contains(item.Id);
        if (!isHidden && item.Status != UserAnimeStatus.None) return;

        _settingsService.Update(settings =>
        {
            var list = settings.UI.HiddenSeasonalIds ??= new List<int>();
            if (isHidden)
            {
                _hiddenSeasonalIds.Remove(item.Id);
                list.Remove(item.Id);
                item.IsHiddenInSeasons = false;
            }
            else
            {
                _hiddenSeasonalIds.Add(item.Id);
                if (!list.Contains(item.Id)) list.Add(item.Id);
                item.IsHiddenInSeasons = true;
            }
        }, save: false);
        _ = _settingsService.SaveAsync();
        ApplyFilters();
    }

    private void SaveSettingsDebounced()
    {
        if (_isInitializing) return;
        _filterDebouncer.Invoke();
    }

    [RelayCommand]
    public void NextSeason()
    {
        int idx = Seasons.IndexOf(CurrentSeason);
        if (idx == 3) { CurrentSeason = Seasons[0]; CurrentYear++; }
        else { CurrentSeason = Seasons[idx + 1]; }
    }

    [RelayCommand]
    public void PreviousSeason()
    {
        int idx = Seasons.IndexOf(CurrentSeason);
        if (idx == 0) { CurrentSeason = Seasons[3]; CurrentYear--; }
        else { CurrentSeason = Seasons[idx - 1]; }
    }

    partial void OnCurrentYearChanged(int value)
    {
        if (_isInitializing) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    partial void OnCurrentSeasonChanged(string value)
    {
        if (_isInitializing) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    partial void OnSortByChanged(string value) => ApplyFilters();
    partial void OnFilterNotInListChanged(bool value) => ApplyFilters();
    partial void OnFilterWatchingChanged(bool value) => ApplyFilters();
    partial void OnFilterCompletedChanged(bool value) => ApplyFilters();
    partial void OnFilterOnHoldChanged(bool value) => ApplyFilters();
    partial void OnFilterPlanToWatchChanged(bool value) => ApplyFilters();
    partial void OnFilterDroppedChanged(bool value) => ApplyFilters();
    partial void OnFilterNsfwChanged(bool value) => ApplyFilters();
    partial void OnShowHiddenChanged(bool value) => ApplyFilters();

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
