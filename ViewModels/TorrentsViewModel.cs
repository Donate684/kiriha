using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;

namespace Kiriha.ViewModels;

public partial class TorrentsViewModel : ViewModelBase
{
    private readonly RssFeedService _rssService;
    private readonly AnimeService _animeService;
    private readonly SettingsService _settingsService;

    public ObservableCollection<TorrentItem> Torrents { get; } = new();

    public ObservableCollection<TorrentGroup> GroupedTorrents { get; } = new();

    public ObservableCollection<AnimeItem> WatchingAnime { get; } = new();

    public ObservableCollection<HideableAnimeItem> HideMenuItems { get; } = new();

    public static TorrentSortMode[] AvailableSortModes { get; } =
        new[] { TorrentSortMode.Newest, TorrentSortMode.Matched, TorrentSortMode.ReleaseGroup };

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _onlyCrunchyroll;

    partial void OnOnlyCrunchyrollChanged(bool value) => PersistFilter(nameof(OnlyCrunchyroll), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterNetflix;

    partial void OnFilterNetflixChanged(bool value) => PersistFilter(nameof(FilterNetflix), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterAmazon;

    partial void OnFilterAmazonChanged(bool value) => PersistFilter(nameof(FilterAmazon), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterHidive;

    partial void OnFilterHidiveChanged(bool value) => PersistFilter(nameof(FilterHidive), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterVaryg;

    partial void OnFilterVarygChanged(bool value) => PersistFilter(nameof(FilterVaryg), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterEraiRaws;

    partial void OnFilterEraiRawsChanged(bool value) => PersistFilter(nameof(FilterEraiRaws), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterToonsHub;

    partial void OnFilterToonsHubChanged(bool value) => PersistFilter(nameof(FilterToonsHub), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterHevc;

    partial void OnFilterHevcChanged(bool value) => PersistFilter(nameof(FilterHevc), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filter1080p;

    partial void OnFilter1080pChanged(bool value) => PersistFilter(nameof(Filter1080p), value);

    [ObservableProperty]
    private AnimeItem? _selectedAnime;

    [ObservableProperty]
    private bool _isHideMode;

    [ObservableProperty]
    private bool _filtersPerTitle;

    /// <summary>Set during bulk-load of filter values so partial change handlers don't persist back.</summary>
    private bool _suppressFilterPersist;

    partial void OnFiltersPerTitleChanged(bool value)
    {
        _settingsService.Update(settings => settings.Torrents.FiltersPerTitle = value);
        ReloadFiltersForCurrentContext();
    }

    private void PersistFilter(string name, bool value)
    {
        if (_suppressFilterPersist) return;

        _settingsService.Update(settings =>
        {
            var cfg = settings.Torrents;
            AppSettings.TorrentFilterSet target;
            if (FiltersPerTitle && SelectedAnime != null)
            {
                if (!cfg.PerTitleFilters.TryGetValue(SelectedAnime.Id, out target!))
                {
                    target = new AppSettings.TorrentFilterSet();
                    cfg.PerTitleFilters[SelectedAnime.Id] = target;
                }
            }
            else
            {
                target = new AppSettings.TorrentFilterSet
                {
                    OnlyCrunchyroll = cfg.OnlyCrunchyroll,
                    FilterNetflix = cfg.FilterNetflix,
                    FilterAmazon = cfg.FilterAmazon,
                    FilterHidive = cfg.FilterHidive,
                    FilterVaryg = cfg.FilterVaryg,
                    FilterEraiRaws = cfg.FilterEraiRaws,
                    FilterToonsHub = cfg.FilterToonsHub,
                    FilterHevc = cfg.FilterHevc,
                    Filter1080p = cfg.Filter1080p,
                };
            }

            switch (name)
            {
                case nameof(OnlyCrunchyroll): target.OnlyCrunchyroll = value; break;
                case nameof(FilterNetflix): target.FilterNetflix = value; break;
                case nameof(FilterAmazon): target.FilterAmazon = value; break;
                case nameof(FilterHidive): target.FilterHidive = value; break;
                case nameof(FilterVaryg): target.FilterVaryg = value; break;
                case nameof(FilterEraiRaws): target.FilterEraiRaws = value; break;
                case nameof(FilterToonsHub): target.FilterToonsHub = value; break;
                case nameof(FilterHevc): target.FilterHevc = value; break;
                case nameof(Filter1080p): target.Filter1080p = value; break;
            }

            if (!(FiltersPerTitle && SelectedAnime != null))
            {
                cfg.OnlyCrunchyroll = target.OnlyCrunchyroll;
                cfg.FilterNetflix = target.FilterNetflix;
                cfg.FilterAmazon = target.FilterAmazon;
                cfg.FilterHidive = target.FilterHidive;
                cfg.FilterVaryg = target.FilterVaryg;
                cfg.FilterEraiRaws = target.FilterEraiRaws;
                cfg.FilterToonsHub = target.FilterToonsHub;
                cfg.FilterHevc = target.FilterHevc;
                cfg.Filter1080p = target.Filter1080p;
            }
        });
        PerformSearchCommand.Execute(null);
    }

    private void ReloadFiltersForCurrentContext()
    {
        var cfg = _settingsService.Current.Torrents;
        AppSettings.TorrentFilterSet src;
        if (FiltersPerTitle && SelectedAnime != null && cfg.PerTitleFilters.TryGetValue(SelectedAnime.Id, out var saved))
        {
            src = saved;
        }
        else if (FiltersPerTitle && SelectedAnime != null)
        {
            // First time this title is opened in per-title mode: start with blank filters.
            src = new AppSettings.TorrentFilterSet();
        }
        else
        {
            src = new AppSettings.TorrentFilterSet
            {
                OnlyCrunchyroll = cfg.OnlyCrunchyroll,
                FilterNetflix = cfg.FilterNetflix,
                FilterAmazon = cfg.FilterAmazon,
                FilterHidive = cfg.FilterHidive,
                FilterVaryg = cfg.FilterVaryg,
                FilterEraiRaws = cfg.FilterEraiRaws,
                FilterToonsHub = cfg.FilterToonsHub,
                FilterHevc = cfg.FilterHevc,
                Filter1080p = cfg.Filter1080p,
            };
        }

        _suppressFilterPersist = true;
        try
        {
            OnlyCrunchyroll = src.OnlyCrunchyroll;
            FilterNetflix = src.FilterNetflix;
            FilterAmazon = src.FilterAmazon;
            FilterHidive = src.FilterHidive;
            FilterVaryg = src.FilterVaryg;
            FilterEraiRaws = src.FilterEraiRaws;
            FilterToonsHub = src.FilterToonsHub;
            FilterHevc = src.FilterHevc;
            Filter1080p = src.Filter1080p;
        }
        finally
        {
            _suppressFilterPersist = false;
        }
    }

    [RelayCommand]
    public void ToggleHideMode() => IsHideMode = !IsHideMode;

    [RelayCommand]
    public void ToggleHideAnime(HideableAnimeItem? item)
    {
        if (item == null) return;
        item.IsHidden = !item.IsHidden;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private TorrentSortMode _sortMode = TorrentSortMode.Newest;

    partial void OnSortModeChanged(TorrentSortMode value)
    {
        RebuildGroupedTorrents();
    }

    public bool HasActiveFilters =>
        FilterVaryg || FilterEraiRaws || FilterToonsHub || Filter1080p || FilterHevc
        || OnlyCrunchyroll || FilterNetflix || FilterAmazon || FilterHidive;

    public TorrentsViewModel(RssFeedService rssService, AnimeService animeService, SettingsService settingsService)
    {
        _rssService = rssService;
        _animeService = animeService;
        _settingsService = settingsService;
        
        _onlyCrunchyroll = _settingsService.Current.Torrents.OnlyCrunchyroll;
        _filterNetflix = _settingsService.Current.Torrents.FilterNetflix;
        _filterAmazon = _settingsService.Current.Torrents.FilterAmazon;
        _filterHidive = _settingsService.Current.Torrents.FilterHidive;
        _filterVaryg = _settingsService.Current.Torrents.FilterVaryg;
        _filterEraiRaws = _settingsService.Current.Torrents.FilterEraiRaws;
        _filterToonsHub = _settingsService.Current.Torrents.FilterToonsHub;
        _filterHevc = _settingsService.Current.Torrents.FilterHevc;
        _filter1080p = _settingsService.Current.Torrents.Filter1080p;
        _filtersPerTitle = _settingsService.Current.Torrents.FiltersPerTitle;

        Torrents.CollectionChanged += (_, _) => RebuildGroupedTorrents();
        
        // Initial sync of watching list
        RefreshWatchingList();
    }

    public void RefreshWatchingList()
    {
        var hidden = new HashSet<int>(_settingsService.Current.Torrents.HiddenAnimeIds ?? new List<int>());
        var watching = _animeService.Collection.Where(x => x.Status == UserAnimeStatus.Watching).ToList();

        WatchingAnime.Clear();
        foreach (var a in watching)
            if (!hidden.Contains(a.Id)) WatchingAnime.Add(a);

        HideMenuItems.Clear();
        foreach (var a in watching)
        {
            var item = new HideableAnimeItem(a, hidden.Contains(a.Id));
            item.HiddenChanged += OnHideMenuItemChanged;
            HideMenuItems.Add(item);
        }
    }

    private void OnHideMenuItemChanged(HideableAnimeItem item)
    {
        _settingsService.Update(settings =>
        {
            var ids = settings.Torrents.HiddenAnimeIds ??= new List<int>();
            if (item.IsHidden)
            {
                if (!ids.Contains(item.Anime.Id)) ids.Add(item.Anime.Id);
            }
            else
            {
                ids.Remove(item.Anime.Id);
            }
        });

        // Sync visible sidebar list without rebuilding HideMenuItems (keep flyout state)
        if (item.IsHidden)
        {
            var existing = WatchingAnime.FirstOrDefault(a => a.Id == item.Anime.Id);
            if (existing != null) WatchingAnime.Remove(existing);
        }
        else if (!WatchingAnime.Any(a => a.Id == item.Anime.Id))
        {
            WatchingAnime.Add(item.Anime);
        }
    }

    partial void OnSelectedAnimeChanged(AnimeItem? value)
    {
        if (FiltersPerTitle)
        {
            // Load this anime's saved filter set (or blank) without triggering persistence.
            ReloadFiltersForCurrentContext();
        }

        if (value != null)
        {
            SearchQuery = value.Title;
            PerformSearchCommand.Execute(null);
        }
    }

    [RelayCommand]
    public async Task PerformSearch()
    {
        string query = SearchQuery?.Trim() ?? string.Empty;
        
        if (FilterVaryg) query = string.IsNullOrEmpty(query) ? "VARYG" : $"{query} VARYG";
        if (FilterEraiRaws) query = string.IsNullOrEmpty(query) ? "Erai-raws" : $"{query} Erai-raws";
        if (FilterToonsHub) query = string.IsNullOrEmpty(query) ? "ToonsHub" : $"{query} ToonsHub";
        if (Filter1080p) query = string.IsNullOrEmpty(query) ? "1080p" : $"{query} 1080p";
        if (FilterHevc) query = string.IsNullOrEmpty(query) ? "HEVC" : $"{query} HEVC";
        if (OnlyCrunchyroll) query = string.IsNullOrEmpty(query) ? "CR" : $"{query} CR";
        if (FilterNetflix) query = string.IsNullOrEmpty(query) ? "NF" : $"{query} NF";
        if (FilterAmazon) query = string.IsNullOrEmpty(query) ? "AMZN" : $"{query} AMZN";
        if (FilterHidive) query = string.IsNullOrEmpty(query) ? "HIDIVE" : $"{query} HIDIVE";

        if (string.IsNullOrEmpty(query))
        {
            Serilog.Log.Debug("Torrents: Query is empty, clearing results");
            Torrents.Clear();
            return;
        }

        try 
        {
            IsLoading = true;
            Serilog.Log.Information("Torrents: Starting search for: {Query}", query);
            
            var results = await _rssService.SearchTorrentsAsync(query);
            Serilog.Log.Information("Torrents: Search returned {Count} items", results.Count);
            
            Torrents.Clear();
            foreach (var r in results) Torrents.Add(r);
            RebuildGroupedTorrents();
            
            if (!results.Any())
            {
                Serilog.Log.Warning("Torrents: No results found for: {Query}", query);
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Torrents: Search failed for: {Query}", query);
        }
        finally 
        {
            IsLoading = false;
            Serilog.Log.Debug("Torrents: Search process completed (loading set to false)");
        }
    }

    [RelayCommand]
    public void SelectAnime(AnimeItem? anime)
    {
        SelectedAnime = anime;
    }

    [RelayCommand]
    public void DownloadMagnet(TorrentItem torrent)
    {
        if (torrent == null || string.IsNullOrEmpty(torrent.MagnetLink)) return;
        UIUtils.OpenUrl(torrent.MagnetLink);
    }

    [RelayCommand]
    public void DownloadTorrentFile(TorrentItem torrent)
    {
        if (torrent == null || string.IsNullOrEmpty(torrent.DownloadLink)) return;
        UIUtils.OpenUrl(torrent.DownloadLink);
    }

    [RelayCommand]
    public void Refresh()
    {
        // RssFeedService checks automatically, but we could trigger a manual check here if needed
        // For now, it's enough to just show what we have
    }

    [RelayCommand]
    public void ClearSelectedAnime()
    {
        SelectedAnime = null;
        SearchQuery = string.Empty;
        Torrents.Clear();
        RebuildGroupedTorrents();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        FilterVaryg = false;
        FilterEraiRaws = false;
        FilterToonsHub = false;
        Filter1080p = false;
        FilterHevc = false;
        OnlyCrunchyroll = false;
        FilterNetflix = false;
        FilterAmazon = false;
        FilterHidive = false;
    }

    private void RebuildGroupedTorrents()
    {
        GroupedTorrents.Clear();

        IEnumerable<TorrentItem> source = Torrents;
        source = SortMode switch
        {
            TorrentSortMode.Matched =>
                source.OrderByDescending(t => t.IsMatched)
                      .ThenByDescending(t => t.PublishDate),
            TorrentSortMode.ReleaseGroup =>
                source.OrderBy(t => string.IsNullOrEmpty(t.ReleaseGroup) ? "zzz" : t.ReleaseGroup,
                               System.StringComparer.OrdinalIgnoreCase)
                      .ThenByDescending(t => t.PublishDate),
            _ => source.OrderByDescending(t => t.PublishDate),
        };

        var groups = source
            .GroupBy(t => string.IsNullOrWhiteSpace(t.AnimeTitle) ? "—" : t.AnimeTitle!.Trim(),
                     System.StringComparer.OrdinalIgnoreCase)
            .Select(g => new TorrentGroup(g.Key, g.ToList()))
            .OrderByDescending(g => g.HasMatch)
            .ThenByDescending(g => g.LatestDate);

        foreach (var g in groups) GroupedTorrents.Add(g);
    }
}

public enum TorrentSortMode
{
    Newest,
    Matched,
    ReleaseGroup,
}

public sealed class HideableAnimeItem : ObservableObject
{
    public HideableAnimeItem(AnimeItem anime, bool isHidden)
    {
        Anime = anime;
        _isHidden = isHidden;
    }

    public AnimeItem Anime { get; }

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (SetProperty(ref _isHidden, value))
                HiddenChanged?.Invoke(this);
        }
    }

    public event System.Action<HideableAnimeItem>? HiddenChanged;
}

public sealed class TorrentGroup
{
    public TorrentGroup(string animeTitle, IReadOnlyList<TorrentItem> items)
    {
        AnimeTitle = animeTitle;
        Items = items;
        HasMatch = items.Any(i => i.IsMatched);
        LatestDate = items.Count > 0 ? items.Max(i => i.PublishDate) : System.DateTime.MinValue;
    }

    public string AnimeTitle { get; }
    public IReadOnlyList<TorrentItem> Items { get; }
    public int Count => Items.Count;
    public bool HasMatch { get; }
    public System.DateTime LatestDate { get; }
}

