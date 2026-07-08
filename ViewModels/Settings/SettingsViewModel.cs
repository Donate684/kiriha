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
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services;
using Kiriha.Services.Api;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Views;
using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly MalAuthService _authService;
    private readonly ShikiAuthService _shikiAuthService;
    private readonly ShikiHostResolver _shikiHostResolver;
    private readonly AnimeListViewModel _animeListViewModel;
    private readonly LocalizationService _localizationService;
    private readonly UpdateService _updateService;
    private readonly CacheCleanupService _cacheCleanupService;
    private readonly ImageCacheService _imageCacheService;
    private readonly MappingService _mappingService;
    private readonly SeasonalViewModel _seasonalViewModel;

    public record ThemeOption(string Name, ThemeType Value);

    // Per-mirror connection state. Only one Shiki mirror can be active at a time
    // (because their accounts/tokens are independent OAuth realms).
    public bool IsShikiOneConnected => _settingsService.Current.Api.Shiki?.Mirror == ShikiMirror.One;
    public bool IsShikiNetConnected => _settingsService.Current.Api.Shiki?.Mirror == ShikiMirror.Net;

    // A login button is clickable only when:
    //   - MAL is connected (master condition the user requested),
    //   - and the *other* mirror isn't already connected.
    public bool CanLoginShikiOne => IsLoggedIn && !IsShikiNetConnected;
    public bool CanLoginShikiNet => IsLoggedIn && !IsShikiOneConnected;

    public List<ThemeOption> AvailableThemes => new() 
    { 
        new ThemeOption(UIUtils.GetLoc("settings.theme.default"), ThemeType.System), 
        new ThemeOption(UIUtils.GetLoc("settings.theme.light"), ThemeType.Light), 
        new ThemeOption(UIUtils.GetLoc("settings.theme.dark"), ThemeType.Dark) 
    };

    [ObservableProperty]
    private ThemeOption _selectedTheme;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiOne))]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiNet))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginOneCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginNetCommand))]
    private bool _isLoggedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiOne))]
    [NotifyPropertyChangedFor(nameof(CanLoginShikiNet))]
    [NotifyPropertyChangedFor(nameof(IsShikiOneConnected))]
    [NotifyPropertyChangedFor(nameof(IsShikiNetConnected))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginOneCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShikiLoginNetCommand))]
    private bool _isShikiLoggedIn;
    
    [ObservableProperty]
    private bool _useRussianTitles;

    [ObservableProperty]
    private bool _useRussianDescriptions;

    [ObservableProperty]
    private bool _showAiringInfo;

    [ObservableProperty]
    private bool _enableMica;

    public bool IsMicaSupported => Platform.IsMicaSupported;

    // Updates
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _newVersion;

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    private int _updateProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadReady))]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private bool _isUpdateDownloaded;

    public bool IsDownloadReady => IsUpdateAvailable && !IsUpdateDownloaded && UpdateProgress == 0;
    public bool IsDownloading => UpdateProgress > 0 && !IsUpdateDownloaded;

    // System
    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _enableScrobbler;

    [ObservableProperty]
    private decimal? _scrobbleDelaySeconds;

    [ObservableProperty]
    private bool _scrobbleNotifyOnSkip;

    [ObservableProperty]
    private bool _enableDiscordRPC;

    [ObservableProperty]
    private bool _autoCheckUpdates;

    [ObservableProperty]
    private bool _autoDownloadUpdates;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClearSelectedCache))]
    [NotifyCanExecuteChangedFor(nameof(ClearSelectedCacheCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCacheStatsCommand))]
    private bool _isCacheBusy;

    [ObservableProperty]
    private string _cacheStatus = string.Empty;

    public ObservableCollection<CacheCleanupItem> CacheItems { get; } = new()
    {
        new CacheCleanupItem(CacheCleanupTarget.History, "settings.cache.items.history"),
        new CacheCleanupItem(CacheCleanupTarget.ImageFiles, "settings.cache.items.images"),
        new CacheCleanupItem(CacheCleanupTarget.ApiCache, "settings.cache.items.api"),
        new CacheCleanupItem(CacheCleanupTarget.RecognitionCache, "settings.cache.items.recognition"),
        new CacheCleanupItem(CacheCleanupTarget.SeasonalCache, "settings.cache.items.seasonal")
    };

    public bool CanClearSelectedCache => !IsCacheBusy && CacheItems.Any(x => x.IsSelected);

    // Notifications
    [ObservableProperty]
    private bool _notifyNewEpisodes;

    [ObservableProperty]
    private bool _notifyAppUpdate;

    [ObservableProperty]
    private decimal? _newEpisodeNotificationDelayMinutes;

    public int EnabledPlayersCount => _settingsService.Current.System.Scrobbler.AllowedProcesses.Count;

    public List<LanguageOption> AvailableLanguages { get; } = new()
    {
        new LanguageOption(Constants.Languages.EnName, Constants.Languages.En),
        new LanguageOption(Constants.Languages.RuName, Constants.Languages.Ru)
    };

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _autoLaunch;

    [ObservableProperty]
    private bool _launchMinimized;

    [ObservableProperty]
    private bool _isSystemPlayer;

    [ObservableProperty]
    private bool _keepPlayerProcessAlive;

    [ObservableProperty]
    private bool _singlePlayerWindow = true;

    [ObservableProperty]
    private string _mpvHwdec = "auto";

    [ObservableProperty]
    private string _mpvVideoOutput = "gpu-next";

    [ObservableProperty]
    private string _mpvGpuApi = "auto";

    [ObservableProperty]
    private string _mpvGpuContext = "auto";

    /// <summary>
    /// Live, two-way bound list of user-defined share buttons. Edits are
    /// persisted via <see cref="HookCustomLink"/> (item-level PropertyChanged)
    /// and <see cref="OnCustomLinksCollectionChanged"/> (add/remove). The
    /// underlying <c>_settingsService.Current.CustomLinks</c> list IS this
    /// collection's backing storage.
    /// </summary>
    public ObservableCollection<CustomShareLink> CustomLinks { get; } = new();

    private readonly AnisthesiaService _anisthesiaService;
    private readonly DiscordService _discordService;
    private readonly SystemIntegrationService _systemIntegrationService;

    public SettingsViewModel(
        SettingsService settingsService,
        MalAuthService authService,
        ShikiAuthService shikiAuthService,
        ShikiHostResolver shikiHostResolver,
        AnimeListViewModel animeListViewModel,
        LocalizationService localizationService,
        UpdateService updateService,
        AnisthesiaService anisthesiaService,
        DiscordService discordService,
        CacheCleanupService cacheCleanupService,
        ImageCacheService imageCacheService,
        MappingService mappingService,
        SeasonalViewModel seasonalViewModel,
        SystemIntegrationService systemIntegrationService)
    {
        _settingsService = settingsService;
        _authService = authService;
        _shikiAuthService = shikiAuthService;
        _shikiHostResolver = shikiHostResolver;
        _animeListViewModel = animeListViewModel;
        _localizationService = localizationService;
        _updateService = updateService;
        _cacheCleanupService = cacheCleanupService;
        _anisthesiaService = anisthesiaService;
        _discordService = discordService;
        _imageCacheService = imageCacheService;
        _mappingService = mappingService;
        _seasonalViewModel = seasonalViewModel;
        _systemIntegrationService = systemIntegrationService;

        // Update state
        IsUpdateAvailable = _updateService.IsUpdateAvailable;
        NewVersion = _updateService.NewVersion;

        // Load existing settings
        AutoLaunch = _settingsService.Current.System.AutoLaunch;
        LaunchMinimized = _settingsService.Current.System.LaunchMinimized;

        _selectedLanguage = AvailableLanguages.FirstOrDefault(x => x.Code == _settingsService.Current.UI.LanguageCode) ?? AvailableLanguages[0];
        _selectedTheme = AvailableThemes.FirstOrDefault(x => x.Value == _settingsService.Current.UI.Theme) ?? AvailableThemes[0];

        
        IsLoggedIn = _settingsService.Current.Api.Mal != null;
        IsShikiLoggedIn = _settingsService.Current.Api.Shiki != null;
        UseRussianTitles = _settingsService.Current.UI.UseRussianTitles;
        UseRussianDescriptions = _settingsService.Current.UI.UseRussianDescriptions;
        ShowAiringInfo = _settingsService.Current.UI.ShowAiringInfo;
        CloseToTray = _settingsService.Current.System.CloseToTray;
        MinimizeToTray = _settingsService.Current.System.MinimizeToTray;
        EnableScrobbler = _settingsService.Current.System.Scrobbler.Enabled;
        ScrobbleDelaySeconds = _settingsService.Current.System.Scrobbler.DelaySeconds;
        ScrobbleNotifyOnSkip = _settingsService.Current.System.Scrobbler.NotifyOnSkippedEpisode;
        EnableDiscordRPC = _settingsService.Current.System.EnableDiscordRPC;
        AutoCheckUpdates = _settingsService.Current.System.AutoCheckUpdates;
        AutoDownloadUpdates = _settingsService.Current.System.AutoDownloadUpdates;
        NotifyNewEpisodes = _settingsService.Current.System.NotifyNewEpisodes;
        NotifyAppUpdate = _settingsService.Current.System.NotifyAppUpdate;
        NewEpisodeNotificationDelayMinutes = _settingsService.Current.System.NewEpisodeNotificationDelayMinutes;
        EnableMica = _settingsService.Current.UI.EnableMica;
        KeepPlayerProcessAlive = _settingsService.Current.System.KeepPlayerProcessAlive;
        SinglePlayerWindow = _settingsService.Current.Player.SingleWindow;
        MpvHwdec = NormalizeMpvOption(_settingsService.Current.Player.MpvHwdec, "auto");
        MpvVideoOutput = NormalizeMpvOption(_settingsService.Current.Player.MpvVideoOutput, "gpu-next");
        MpvGpuApi = NormalizeMpvOption(_settingsService.Current.Player.MpvGpuApi, "auto");
        MpvGpuContext = NormalizeMpvOption(_settingsService.Current.Player.MpvGpuContext, "auto");

        IsSystemPlayer = _systemIntegrationService.IsRegistered();

        InitializeCustomLinks();
        InitializeCacheItems();
        _ = RefreshCacheStats();
    }

    private void InitializeCacheItems()
    {
        foreach (var item in CacheItems)
        {
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CacheCleanupItem.IsSelected))
                {
                    OnPropertyChanged(nameof(CanClearSelectedCache));
                    ClearSelectedCacheCommand.NotifyCanExecuteChanged();
                }
            };
        }
    }

}
