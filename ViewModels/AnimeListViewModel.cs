using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Converters;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.ViewModels;

public partial class AnimeListViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AnimeService _animeService;
    private readonly LoadQueueService _queueService;
    private readonly AiringInfoService _airingInfoService;
    private readonly RssFeedService _rssService;

    public ObservableCollection<AnimeItem> AnimeItems => _animeService.Collection;

    [ObservableProperty] private AvaloniaList<AnimeItem> _filteredItems = new();
    [ObservableProperty] private string _watchingHeader = string.Empty;
    [ObservableProperty] private string _completedHeader = string.Empty;
    [ObservableProperty] private string _onHoldHeader = string.Empty;
    [ObservableProperty] private string _droppedHeader = string.Empty;
    [ObservableProperty] private string _planToWatchHeader = string.Empty;

    [ObservableProperty] private AnimeItem? _selectedItem;
    [ObservableProperty] private bool _isBusy;

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            SetProperty(ref _searchQuery, value);
            _searchDebouncer?.Invoke();
        }
    }
    private Utils.Debouncer? _searchDebouncer;
    private Utils.Debouncer? _collectionChangeDebouncer;

    // Per-minute ticker that re-evaluates the airing countdown ("Hч Mм") on
    // every visible card. Without it the pill text would only refresh when
    // NextEpisodeAt itself changed (i.e. on the next 12 h sync), so a card
    // could sit stuck on "3ч 19м" for hours.
    private DispatcherTimer? _airingTicker;

    public object[] AvailableScores => _settingsService.Current.UI.UseFiveStarRating 
        ? new object[] 
        { 
            RatingHelper.GetRatingOption("-"),
            RatingHelper.GetRatingOption("10"),
            RatingHelper.GetRatingOption("9"),
            RatingHelper.GetRatingOption("7"),
            RatingHelper.GetRatingOption("5"),
            RatingHelper.GetRatingOption("3"),
            RatingHelper.GetRatingOption("1")
        }
        : new object[] 
        { 
            RatingHelper.GetRatingOption("-"),
            RatingHelper.GetRatingOption("10"),
            RatingHelper.GetRatingOption("9"),
            RatingHelper.GetRatingOption("8"),
            RatingHelper.GetRatingOption("7"),
            RatingHelper.GetRatingOption("6"),
            RatingHelper.GetRatingOption("5"),
            RatingHelper.GetRatingOption("4"),
            RatingHelper.GetRatingOption("3"),
            RatingHelper.GetRatingOption("2"),
            RatingHelper.GetRatingOption("1")
        };

    private string GetLoc(string key) => UIUtils.GetLoc(key);

    public void RefreshAvailableScores()
    {
        OnPropertyChanged(nameof(AvailableScores));
        OnPropertyChanged(nameof(UseFiveStarRating));
    }

    public bool UseFiveStarRating => _settingsService.Current.UI.UseFiveStarRating;

    [ObservableProperty] private AnimeItem? _activeItem;

    // Sorting
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySortBy))]
    private string _sortBy = "Title";
    public string DisplaySortBy => UIUtils.GetLoc("filters.sort." + SortBy.ToLower());
    public List<string> SortOptions { get; } = new() { "Title", "RussianTitle", "Score", "Progress", "Date", "Popularity" };

    // Filters
    [ObservableProperty] private bool _filterNsfw;
    [ObservableProperty] private bool _isFilterActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWatchingSelected))]
    [NotifyPropertyChangedFor(nameof(IsCompletedSelected))]
    [NotifyPropertyChangedFor(nameof(IsOnHoldSelected))]
    [NotifyPropertyChangedFor(nameof(IsDroppedSelected))]
    [NotifyPropertyChangedFor(nameof(IsPlanToWatchSelected))]
    private UserAnimeStatus _selectedStatus = UserAnimeStatus.Watching;

    public bool IsWatchingSelected => SelectedStatus == UserAnimeStatus.Watching;
    public bool IsCompletedSelected => SelectedStatus == UserAnimeStatus.Completed;
    public bool IsOnHoldSelected => SelectedStatus == UserAnimeStatus.OnHold;
    public bool IsDroppedSelected => SelectedStatus == UserAnimeStatus.Dropped;
    public bool IsPlanToWatchSelected => SelectedStatus == UserAnimeStatus.PlanToWatch;

    public AnimeListViewModel(SettingsService settingsService, AnimeService animeService, LoadQueueService queueService, AiringInfoService airingInfoService, RssFeedService rssService)
    {
        _settingsService = settingsService;
        _animeService = animeService;
        _queueService = queueService;
        _airingInfoService = airingInfoService;
        _rssService = rssService;

        _filterNsfw = _settingsService.Current.UI.ListShowNsfw;
        _sortBy = _settingsService.Current.UI.ListSortBy;
        IsFilterActive = _filterNsfw; 

        _searchDebouncer = new Utils.Debouncer(TimeSpan.FromMilliseconds(300), () => {
             Dispatcher.UIThread.Post(async () => await ApplyCurrentFiltersAsync());
        });

        // Debouncer for CollectionChanged: airing/seasonal sync inserts 30+ new
        // anime in a tight burst (each going through a UI-thread Add). Without
        // debouncing, OnCollectionChanged would post UpdateCounts +
        // ApplyCurrentFilters per item, scanning 2400+ entries every time.
        // 200 ms collapses a burst into a single refresh.
        _collectionChangeDebouncer = new Utils.Debouncer(TimeSpan.FromMilliseconds(200), () => {
            Dispatcher.UIThread.Post(async () => {
                await UpdateCountsAsync();
                await ApplyCurrentFiltersAsync();
            });
        });

        _animeService.Collection.CollectionChanged += OnCollectionChanged;

        // Tick once a minute on the UI thread to refresh the airing countdown.
        // Cheap: each tick only iterates currently-loaded items and emits
        // PropertyChanged for the three time-dependent badge properties.
        _airingTicker = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, OnAiringTick);
        _airingTicker.Start();

        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) => {
            Dispatcher.UIThread.Post(() => ((AnimeListViewModel)r).RefreshAfterDetailsEdit());
        });

        RefreshLocalization();
        InitializeAsync().SafeFireAndForget("InitializeAsync");
    }

    partial void OnSortByChanged(string value)
    {
        _settingsService.Update(settings => settings.UI.ListSortBy = value);
        Dispatcher.UIThread.Post(async () => await ApplyCurrentFiltersAsync());
    }

    partial void OnFilterNsfwChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.ListShowNsfw = value);
        IsFilterActive = value;
        Dispatcher.UIThread.Post(async () => await ApplyCurrentFiltersAsync());
    }

    [RelayCommand]
    public void ClearFilters()
    {
        FilterNsfw = false;
        IsFilterActive = false;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_animeService.IsSyncing || _animeService.IsInitializing)
        {
            // During mass sync/init, we'll trigger one big update at the end 
            // instead of hundreds of individual UI refreshes.
            return;
        }

        Log.Debug("AnimeListViewModel: CollectionChanged action={Action}", e.Action);

        // Coalesce bursts: airing/Shikimori sync can fire 30+ Add events in a
        // few hundred ms. The debouncer collapses them into a single refresh.
        _collectionChangeDebouncer?.Invoke();
    }

    private void OnAiringTick(object? sender, EventArgs e)
    {
        // Only items with a future-dated next episode (or recently-aired
        // unconfirmed state, i.e. up to 48 h overdue - see AiringBadgeText)
        // need their countdown re-evaluated. Skipping the rest keeps the
        // tick essentially free even on large libraries.
        var now = DateTime.Now;
        foreach (var item in FilteredItems)
        {
            if (item.NextEpisodeAt.HasValue)
            {
                var diff = item.NextEpisodeAt.Value - now;
                if (diff.TotalHours < -48) continue;
                item.RefreshAiringBadge();
            }
            else if (item.IsNewEpisode)
            {
                // The 2-day "new episode" window is also time-dependent.
                item.RefreshAiringBadge();
            }
        }
    }

    public void RefreshLocalization()
    {
        RefreshAvailableScores();
        UpdateCountsAsync().SafeFireAndForget("RefreshLocalization");
        foreach (var item in AnimeItems) item.RefreshMetadata();
    }

    private async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await _animeService.InitializeAsync();
            await UpdateCountsAsync();
            await ApplyCurrentFiltersAsync();

            // If it's a fresh start (no items in DB), trigger a full sync automatically.
            // Otherwise MaintenanceService handles periodic airing sync (threshold 12h, first run ~5 min after startup).
            if (AnimeItems.Count == 0)
            {
                await SyncMal();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void EnqueueItemForViewport(AnimeItem item)
    {
        if (item == null) return;
        _queueService.EnqueueForViewport(new[] { item });
    }

    private async Task UpdateCountsAsync()
    {
        var snapshot = await Dispatcher.UIThread.InvokeAsync(() => _animeService.Collection.ToList());

        // Offload counting to background thread to avoid UI stutters on large collections
        var (watching, completed, onHold, dropped, ptw) = await Task.Run(() => 
        {
            int w = 0, c = 0, h = 0, d = 0, p = 0;
            foreach (var item in snapshot)
            {
                var status = item.Status;
                if (status == UserAnimeStatus.Watching || item.IsRewatching) w++;
                else if (status == UserAnimeStatus.Completed) c++;
                else if (status == UserAnimeStatus.OnHold) h++;
                else if (status == UserAnimeStatus.Dropped) d++;
                else if (status == UserAnimeStatus.PlanToWatch) p++;
            }
            return (w, c, h, d, p);
        });

        WatchingHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.watching"), watching.ToString());
        CompletedHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.completed"), completed.ToString());
        OnHoldHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.on_hold"), onHold.ToString());
        DroppedHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.dropped"), dropped.ToString());
        PlanToWatchHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.plan_to_watch"), ptw.ToString());
    }

    [RelayCommand]
    public async Task SyncMal()
    {
        IsBusy = true;
        try
        {
            bool success = await _animeService.SyncWithTrackersAsync();
            
            if (success)
            {
                await UpdateCountsAsync();
                await ApplyCurrentFiltersAsync();
                
                await _airingInfoService.SyncOngoingEpisodesAsync(force: true);
                await _rssService.CheckFeedsAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual sync failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task Filter(string statusString) 
    {
        if (Enum.TryParse<UserAnimeStatus>(statusString, true, out var parsed))
        {
            await FilterByStatusAsync(parsed);
        }
    }

    public async Task FilterByStatusAsync(UserAnimeStatus status)
    {
        SelectedStatus = status;
        await ApplyCurrentFiltersAsync();
    }

    private async Task ApplyCurrentFiltersAsync()
    {
        var snapshot = await Dispatcher.UIThread.InvokeAsync(() => _animeService.Collection.ToList());

        // Offload LINQ queries to background thread
        var filtered = await Task.Run(() => 
        {
            var query = snapshot.AsEnumerable();

            if (SelectedStatus == UserAnimeStatus.Watching)
                query = query.Where(x => x.Status == UserAnimeStatus.Watching || x.IsRewatching);
            else
                query = query.Where(x => x.Status == SelectedStatus);

            query = query.ApplySearch(SearchQuery)
                         .ApplyNsfw(FilterNsfw)
                         .ApplySorting(SortBy);

            return query.ToList();
        });

        // In-place update of the existing AvaloniaList instead of replacing the
        // reference. Re-assigning FilteredItems would force every binding (and
        // any ItemsRepeater materializing this collection) to detach + rebind
        // the entire visual tree, which is what caused a ~358 ms UI-thread
        // stall right after startup population. Mutating the same instance
        // emits one CollectionChanged(Reset) and lets ItemsRepeater diff in
        // its own incremental pipeline.
        FilteredItems.Clear();
        FilteredItems.AddRange(filtered);
    }

    public void RefreshAfterDetailsEdit()
    {
        UpdateCountsAsync().SafeFireAndForget("RefreshAfterDetailsEdit");
        ApplyCurrentFiltersAsync().SafeFireAndForget("RefreshAfterDetailsEdit");
    }

    [RelayCommand]
    public async Task IncrementProgress(AnimeItem item)
    {
        if (item.TotalEpisodes == 0 || item.Progress < item.TotalEpisodes)
        {
            await SetProgressTo(item, item.Progress + 1);
        }
    }

    public async Task SetProgressTo(AnimeItem item, int nextProgress)
    {
        var oldStatus = item.Status;
        var oldRewatching = item.IsRewatching;
        
        await _animeService.SmartIncrementProgressAsync(item, nextProgress);
        
        await UpdateCountsAsync();
        
        // Only re-filter if the status changed (item needs to move to another tab)
        if (item.Status != oldStatus || item.IsRewatching != oldRewatching)
        {
            await ApplyCurrentFiltersAsync();
        }
    }

    [RelayCommand]
    public async Task DecrementProgress(AnimeItem item)
    {
        await _animeService.SmartDecrementProgressAsync(item);
        await UpdateCountsAsync();
    }

    [RelayCommand]
    public void OpenScoreMenu(AnimeItem item) => ActiveItem = item;

    [RelayCommand]
    public async Task ApplyScoreFromMenu(RatingOption rating)
    {
        if (ActiveItem == null || rating == null) return;
        int.TryParse(rating.Value, out int score);
        await _animeService.SetScoreAsync(ActiveItem, score);
    }

    [RelayCommand]
    public async Task SetScore(AnimeItem item)
    {
        if (item == null) return;
        int.TryParse(item.Score, out int score);
        await _animeService.SetScoreAsync(item, score);
    }

    public void Dispose()
    {
        _searchDebouncer?.Dispose();
        _airingTicker?.Stop();
        _airingTicker = null;
        _animeService.Collection.CollectionChanged -= OnCollectionChanged;
    }
}
