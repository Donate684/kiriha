using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;

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
    private AnimeItem? _selectedAnime;

    [ObservableProperty]
    private bool _isHideMode;

    public TorrentsViewModel(RssFeedService rssService, AnimeService animeService, SettingsService settingsService)
    {
        _rssService = rssService;
        _animeService = animeService;
        _settingsService = settingsService;

        LoadFilterSettings();

        Torrents.CollectionChanged += (_, _) => RebuildGroupedTorrents();
        RefreshWatchingList();
    }
}
