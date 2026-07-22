using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Platform;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Models.Messages;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.ViewModels;

public partial class NowPlayingViewModel : ViewModelBase, IDisposable,
    IRecipient<MediaChangedMessage>,
    IRecipient<AnimeMatchedMessage>,
    IRecipient<TrackingCountdownMessage>,
    IRecipient<TrackingStatusMessage>
{
    private readonly TrackingService _trackingService;
    // Tracks the anime id of an in-flight manual selection. Until the background
    // TrackingService fires AnimeMatched with this id (or null on a media change),
    // we ignore intermediate null/other matches so they don't clobber the UI choice.
    // 0 means "no manual selection pending".
    private int _pendingManualMatchId;
    private readonly SettingsService _settingsService;
    private readonly MappingService _mappingService;
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeProgressService _progressService;
    private readonly SyncManager _syncManager;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly Services.Api.MalApiService _malApi;

    [ObservableProperty] private ParsedMedia? _currentMedia;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotInList))]
    [NotifyPropertyChangedFor(nameof(AllAlternativeTitles))]
    [NotifyPropertyChangedFor(nameof(HasAlternativeTitles))]
    private AnimeItem? _matchedAnime;
    [ObservableProperty] private AnimeItem? _pendingMatch;

    /// <summary>
    /// Resolved user-defined share buttons for the currently matched anime.
    /// Refreshed via <see cref="OnMatchedAnimeChanged"/>.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    partial void OnMatchedAnimeChanged(AnimeItem? value)
    {
        CustomShareLinks.Clear();
        
        if (value == null)
        {
            _allAlternativeTitles = Array.Empty<string>();
            return;
        }

        var list = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(value.EnglishTitle) && value.EnglishTitle != value.Title)
            list.Add(value.EnglishTitle);
        if (!string.IsNullOrEmpty(value.JapaneseTitle) && value.JapaneseTitle != value.Title)
            list.Add(value.JapaneseTitle);

        foreach (var syn in value.AlternativeTitles)
        {
            if (syn != value.Title && !list.Contains(syn))
                list.Add(syn);
        }
        _allAlternativeTitles = list;

        foreach (var link in _settingsService.Current.CustomLinks)
        {
            if (string.IsNullOrWhiteSpace(link.UrlTemplate)) continue;
            var url = Kiriha.Core.CustomLinkResolver.Resolve(link.UrlTemplate, value);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isSearching;

    [ObservableProperty] private bool _isManuallyMapped;

    public bool IsNotInList => MatchedAnime != null && MatchedAnime.Status == UserAnimeStatus.None;

    private System.Collections.Generic.IReadOnlyList<string> _allAlternativeTitles = Array.Empty<string>();
    public System.Collections.Generic.IEnumerable<string> AllAlternativeTitles => _allAlternativeTitles;

    public bool HasAlternativeTitles => AllAlternativeTitles.Any();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _isMediaDetected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string _countdownStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStatus))]
    private string _trackingStatus = string.Empty;

    public string DisplayStatus => !IsMediaDetected ? UIUtils.GetLoc("scrobbler.status.ready") :
                                   (!string.IsNullOrEmpty(TrackingStatus) ? TrackingStatus :
                                   (IsPaused ? UIUtils.GetLoc("scrobbler.status.paused") :
                                   (string.IsNullOrEmpty(CountdownStatus) ? UIUtils.GetLoc("scrobbler.status.active") : CountdownStatus)));

    public SettingsService Settings => _settingsService;
    public bool IsScrobblerEnabled => _settingsService.Current.System.Scrobbler.Enabled;

    public ObservableCollection<string> DetectionLogs { get; } = new();
    public ObservableCollection<AnimeItem> Suggestions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions))]
    private bool _showSuggestions;

    public bool HasSuggestions => ShowSuggestions && Suggestions.Count > 0;

    [ObservableProperty] private bool _isSearchPanelOpen;

    private CancellationTokenSource? _searchCts;
    private readonly CancellationTokenSource _disposeCts = new();

    public NowPlayingViewModel(
        TrackingService trackingService,
        SettingsService settingsService,
        MappingService mappingService,
        AnimeRepository animeRepo,
        AnimeProgressService progressService,
        SyncManager syncManager,
        ShikiMetadataService shikiMetadataService,
        Services.Api.MalApiService malApi)
    {
        _trackingService = trackingService;
        _settingsService = settingsService;
        _mappingService = mappingService;
        _animeRepo = animeRepo;
        _progressService = progressService;
        _syncManager = syncManager;
        _shikiMetadataService = shikiMetadataService;
        _malApi = malApi;

        WeakReferenceMessenger.Default.RegisterAll(this);

        // Sync initial state if any
        CurrentMedia = _trackingService.CurrentMedia;
        MatchedAnime = _trackingService.MatchedAnime;
        IsMediaDetected = CurrentMedia != null;
    }



    public void Dispose()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        try { _searchCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling search CTS during dispose"); }
        try { _searchCts?.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing search CTS"); }
        try { _disposeCts.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling dispose CTS"); }
        try { _disposeCts.Dispose(); } catch (Exception ex) { Log.Debug(ex, "Error disposing dispose CTS"); }
    }
}
