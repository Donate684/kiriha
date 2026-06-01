using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels;

public partial class SeasonalViewModel
{
    private List<AnimeItem> _allSeasonalItems = new();
    private Dictionary<int, UserAnimeStatus> _userAnimeStore = new();
    private HashSet<int> _hiddenSeasonalIds = new();
    private static readonly ConcurrentDictionary<(int, string), List<AnimeItem>> _seasonalCache = new();
    private static int _diskHydrated;
    private bool _isInitializing = true;

    private CancellationTokenSource? _loadCts;
    private bool _isDisposed;
    private readonly Utils.Debouncer _filterDebouncer;
    private readonly Utils.Debouncer _applyFilterDebouncer;
    private int _applyFiltersRequestCount;
    private int _initialLoadStarted;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySeason))]
    private int _currentYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySeason))]
    private string _currentSeason = "";

    public string DisplaySeason => UIUtils.GetLoc("anime.seasons." + CurrentSeason.ToLower());

    [ObservableProperty] private AvaloniaList<AnimeItem> _displayItems = new();
    [ObservableProperty] private string _currentHeader = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplaySortBy))]
    private string _sortBy = "";

    public string DisplaySortBy => UIUtils.GetLoc("filters.sort." + SortBy.ToLower());

    public List<string> SortOptions { get; } = new()
    {
        Constants.Sorting.Popularity,
        Constants.Sorting.Score,
        Constants.Sorting.Title,
        Constants.Sorting.RussianTitle,
        Constants.Sorting.Date
    };

    [ObservableProperty] private string? _searchQuery;

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

    public List<string> Seasons { get; } = new()
    {
        Constants.Seasons.Winter,
        Constants.Seasons.Spring,
        Constants.Seasons.Summer,
        Constants.Seasons.Fall
    };
}
