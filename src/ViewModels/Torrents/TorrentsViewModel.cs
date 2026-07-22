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
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;

namespace Kiriha.ViewModels.Torrents;

public partial class TorrentsViewModel : ViewModelBase
{
    private readonly RssFeedService _rssService;
    private readonly AnimeRepository _animeRepo;
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
    private AnimeItem? _selectedAnime;

    [ObservableProperty]
    private bool _isHideMode;

    public TorrentsViewModel(RssFeedService rssService, AnimeRepository animeRepo, SettingsService settingsService)
    {
        _rssService = rssService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;

        LoadFilterSettings();

        Torrents.CollectionChanged += (_, _) => RebuildGroupedTorrents();
        RefreshWatchingList();
    }
}
