using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeSyncOrchestrator _syncOrchestrator;
    private readonly AnimeProgressService _progressService;
    private readonly LoadQueueService _queueService;
    private readonly AiringInfoService _airingInfoService;
    private readonly RssFeedService _rssService;
    private readonly AppReadinessService _readinessService;
    private readonly Kiriha.Core.Dialogs.IDialogService _dialogService;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly AnimeCollectionProjection _listProjection = new();

    public SettingsService SettingsService => _settingsService;
    public Kiriha.Core.Dialogs.IDialogService DialogService => _dialogService;
    public ShikiMetadataService ShikiMetadataService => _shikiMetadataService;

    public ObservableCollection<AnimeItem> AnimeItems => _animeRepo.Collection;

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
    private Kiriha.Utils.Async.Debouncer? _searchDebouncer;
    private Kiriha.Utils.Async.Debouncer? _collectionChangeDebouncer;
    private Kiriha.Utils.Async.Debouncer? _filterRefreshDebouncer;
    private int _filterRefreshVersion;


    // Per-minute ticker that re-evaluates the airing countdown ("Hч Mм") on
    // every visible card. Without it the pill text would only refresh when
    // NextEpisodeAt itself changed (i.e. on the next 12 h sync), so a card
    // could sit stuck on "3ч 19м" for hours.
    private DispatcherTimer? _airingTicker;

    private object[]? _availableScores;
    public object[] AvailableScores => _availableScores ??= CreateAvailableScores();

    private static object[] CreateAvailableScores() =>
    [
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
    ];

    private string GetLoc(string key) => UIUtils.GetLoc(key);

    public void RefreshAvailableScores()
    {
        _availableScores = CreateAvailableScores();
        OnPropertyChanged(nameof(AvailableScores));
    }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnimeSelected))]
    [NotifyPropertyChangedFor(nameof(IsMangaSelected))]
    private MediaKind _selectedMediaKind = MediaKind.Anime;

    public bool IsAnimeSelected => SelectedMediaKind == MediaKind.Anime;
    public bool IsMangaSelected => SelectedMediaKind == MediaKind.Manga;

    public bool IsWatchingSelected => SelectedStatus == UserAnimeStatus.Watching;
    public bool IsCompletedSelected => SelectedStatus == UserAnimeStatus.Completed;
    public bool IsOnHoldSelected => SelectedStatus == UserAnimeStatus.OnHold;
    public bool IsDroppedSelected => SelectedStatus == UserAnimeStatus.Dropped;
    public bool IsPlanToWatchSelected => SelectedStatus == UserAnimeStatus.PlanToWatch;

    public AnimeListViewModel(
        SettingsService settingsService,
        AnimeRepository animeRepo,
        AnimeSyncOrchestrator syncOrchestrator,
        AnimeProgressService progressService,
        LoadQueueService queueService,
        AiringInfoService airingInfoService,
        RssFeedService rssService,
        AppReadinessService readinessService,
        Kiriha.Core.Dialogs.IDialogService dialogService,
        ShikiMetadataService shikiMetadataService)
    {
        _settingsService = settingsService;
        _animeRepo = animeRepo;
        _syncOrchestrator = syncOrchestrator;
        _progressService = progressService;
        _queueService = queueService;
        _airingInfoService = airingInfoService;
        _rssService = rssService;
        _readinessService = readinessService;
        _dialogService = dialogService;
        _shikiMetadataService = shikiMetadataService;

        _filterNsfw = _settingsService.Current.UI.ListShowNsfw;
        _sortBy = _settingsService.Current.UI.ListSortBy;
        IsFilterActive = _filterNsfw;

        _filterRefreshDebouncer = new Kiriha.Utils.Async.Debouncer(
            TimeSpan.FromMilliseconds(180),
            ApplyCurrentFiltersAsync);

        _searchDebouncer = new Kiriha.Utils.Async.Debouncer(
            TimeSpan.FromMilliseconds(300),
            _ =>
            {
                ScheduleFilterRefresh();
                return Task.CompletedTask;
            });

        // Debouncer for CollectionChanged: airing/seasonal sync inserts 30+ new
        // anime in a tight burst (each going through a UI-thread Add). Without
        // debouncing, OnCollectionChanged would post UpdateCounts +
        // ApplyCurrentFilters per item, scanning 2400+ entries every time.
        // 200 ms collapses a burst into a single refresh.
        _collectionChangeDebouncer = new Kiriha.Utils.Async.Debouncer(TimeSpan.FromMilliseconds(200), async ct =>
        {
            await UpdateCountsAsync();
            await ApplyCurrentFiltersAsync(ct);
        });

        _animeRepo.Collection.CollectionChanged += OnCollectionChanged;

        // Tick once a minute on the UI thread to refresh the airing countdown.
        // Cheap: each tick only iterates currently-loaded items and emits
        // PropertyChanged for the three time-dependent badge properties.
        _airingTicker = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, OnAiringTick);
        _airingTicker.Start();

        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() => ((AnimeListViewModel)r).RefreshAfterDetailsEdit());
        });

        RefreshLocalization();
        _readinessService.StateChanged += OnReadinessStateChanged;
        ObserveReadinessAsync().SafeFireAndForget("ObserveReadinessAsync");
    }

    partial void OnSortByChanged(string value)
    {
        _settingsService.Update(settings => settings.UI.ListSortBy = value, SettingsSection.UI);
        ScheduleFilterRefresh();
    }

    partial void OnFilterNsfwChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.ListShowNsfw = value, SettingsSection.UI);
        IsFilterActive = value;
        ScheduleFilterRefresh();
    }


    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncOrchestrator.IsSyncing || _animeRepo.IsInitializing)
        {
            // During mass sync/init, we'll trigger one big update at the end 
            // instead of hundreds of individual UI refreshes.
            return;
        }

        Log.Debug("AnimeListViewModel: CollectionChanged action={Action}", e.Action);

        // Coalesce bursts: airing/Shikimori sync can fire 30+ Add events in a
        // few hundred ms. The debouncer collapses them into a single refresh.
        _listProjection.ApplyCollectionChange(e, _animeRepo.Collection);
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

    private void OnReadinessStateChanged(object? sender, AppReadinessState state)
    {
        Dispatcher.UIThread.Post(() => IsBusy = state == AppReadinessState.Starting);
    }

    private async Task ObserveReadinessAsync()
    {
        IsBusy = _readinessService.State is AppReadinessState.NotStarted or AppReadinessState.Starting;
        try
        {
            await _readinessService.ReadyTask;
            RebuildListProjection();
            await UpdateCountsAsync();
            await ApplyCurrentFiltersAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeListViewModel: readiness observer failed");
        }
        finally
        {
            IsBusy = false;
        }
    }



    public void Dispose()
    {
        _searchDebouncer?.Dispose();
        _collectionChangeDebouncer?.Dispose();
        _filterRefreshDebouncer?.Dispose();
        _airingTicker?.Stop();
        _airingTicker = null;
        _readinessService.StateChanged -= OnReadinessStateChanged;
        _animeRepo.Collection.CollectionChanged -= OnCollectionChanged;
        _listProjection.Dispose();
    }
}
