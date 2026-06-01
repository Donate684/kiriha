using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Navigation;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Utils;

namespace Kiriha.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IRecipient<NavigationMessage>
{
    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isUpdateDialogOpen;

    [ObservableProperty]
    private UpdateDialogViewModel? _updateDialog;

    public SettingsViewModel? SettingsViewModel => _settingsViewModel;

    private AnimeListViewModel? _animeListViewModel;
    private SettingsViewModel? _settingsViewModel;
    private NowPlayingViewModel? _nowPlayingViewModel;
    private HistoryViewModel? _historyViewModel;
    private TorrentsViewModel? _torrentsViewModel;
    private SeasonalViewModel? _seasonalViewModel;
    private AnalyticsViewModel? _analyticsViewModel;
    
    // IViewModelFactory delivers a fresh transient instance on each navigation —
    // see DI registrations: WelcomeViewModel and SearchViewModel are AddTransient.
    private readonly IViewModelFactory _viewModelFactory;
    
    private readonly Kiriha.Services.Data.SettingsService _settingsService;

    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    public MainWindowViewModel(
        IViewModelFactory viewModelFactory,
        Kiriha.Services.Data.SettingsService settingsService)
    {
        _viewModelFactory = viewModelFactory;
        _settingsService = settingsService;

        // Load saved sidebar state
        IsPaneOpen = _settingsService.Current.UI.IsPaneOpen;
        
        // Register for navigation messages
        WeakReferenceMessenger.Default.Register(this);

        // Start on Welcome page
        NavigateWelcome();
    }

    public void Receive(NavigationMessage message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (message.Page)
            {
                case NavigationPage.Home: NavigateHome(); break;
                case NavigationPage.AnimeList: NavigateAnimeList(); break;
                case NavigationPage.Profile: NavigateAnalytics(); break;
                case NavigationPage.Seasonal: NavigateSeasonal(); break;
                case NavigationPage.History: NavigateHistory(); break;
                case NavigationPage.Torrents: NavigateTorrents(); break;
                case NavigationPage.Search: NavigateSearch(); break;
                case NavigationPage.Settings: NavigateSettings(); break;
                case NavigationPage.Welcome: NavigateWelcome(); break;
            }
        });
    }

    partial void OnIsPaneOpenChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.IsPaneOpen = value, SettingsSection.UI);
    }

    [ObservableProperty]
    private int _selectedNavigationIndex = 0;

    [ObservableProperty]
    private bool _isSettingsSelected = false;

    [ObservableProperty]
    private bool _isNavigationBlocked = false;

    private void SetCurrentPage(ViewModelBase page)
    {
        if (CurrentPage is IDisposable disposable && 
            CurrentPage != _animeListViewModel && 
            CurrentPage != _nowPlayingViewModel &&
            CurrentPage != _historyViewModel &&
            CurrentPage != _torrentsViewModel &&
            CurrentPage != _seasonalViewModel &&
            CurrentPage != _analyticsViewModel)
        {
            disposable.Dispose();
        }
        CurrentPage = page;
    }

    private SettingsViewModel EnsureSettingsViewModel()
    {
        if (_settingsViewModel != null)
            return _settingsViewModel;

        _settingsViewModel = _viewModelFactory.Create<SettingsViewModel>();
        OnPropertyChanged(nameof(SettingsViewModel));
        return _settingsViewModel;
    }

    private AnimeListViewModel EnsureAnimeListViewModel() =>
        _animeListViewModel ??= _viewModelFactory.Create<AnimeListViewModel>();

    private NowPlayingViewModel EnsureNowPlayingViewModel() =>
        _nowPlayingViewModel ??= _viewModelFactory.Create<NowPlayingViewModel>();

    private HistoryViewModel EnsureHistoryViewModel() =>
        _historyViewModel ??= _viewModelFactory.Create<HistoryViewModel>();

    private TorrentsViewModel EnsureTorrentsViewModel() =>
        _torrentsViewModel ??= _viewModelFactory.Create<TorrentsViewModel>();

    private SeasonalViewModel EnsureSeasonalViewModel() =>
        _seasonalViewModel ??= _viewModelFactory.Create<SeasonalViewModel>();

    private AnalyticsViewModel EnsureAnalyticsViewModel() =>
        _analyticsViewModel ??= _viewModelFactory.Create<AnalyticsViewModel>();

    partial void OnSelectedNavigationIndexChanged(int value)
    {
        if (IsNavigationBlocked) 
        {
            // Revert selection if blocked
            return;
        }

        if (value >= 0) IsSettingsSelected = false;
        
        switch (value)
        {
            case 0: NavigateAnalytics(); break;
            case 1: NavigateHome(); break;
            case 2: NavigateAnimeList(); break;
            case 3: NavigateSeasonal(); break;
            case 4: NavigateHistory(); break;
            case 5: NavigateTorrents(); break;
            case 6: NavigateSearch(); break;
        }
    }

    [RelayCommand]
    public void TriggerPane()
    {
        if (IsNavigationBlocked) return;
        IsPaneOpen = !IsPaneOpen;
    }

    [RelayCommand]
    public void NavigateWelcome()
    {
        SelectedNavigationIndex = -1;
        IsSettingsSelected = false;
        SetCurrentPage(_viewModelFactory.Create<WelcomeViewModel>());
    }

    [RelayCommand]
    public void NavigateHome()
    {
        SetCurrentPage(EnsureNowPlayingViewModel());
    }

    [RelayCommand]
    public void NavigateSettings()
    {
        if (IsNavigationBlocked) return;
        EnsureSettingsViewModel();
        IsSettingsOpen = true;
        IsSettingsSelected = true;
    }

    [RelayCommand]
    public void CloseSettings()
    {
        IsSettingsOpen = false;
        IsSettingsSelected = false;
    }

    public void ShowUpdateDialog(bool isDownloaded = false)
    {
        if (IsUpdateDialogOpen) return;
        UpdateDialog = _viewModelFactory.CreateWithArgs<UpdateDialogViewModel>((Action)CloseUpdateDialog, isDownloaded);
        IsUpdateDialogOpen = true;
    }

    [RelayCommand]
    public void CloseUpdateDialog()
    {
        IsUpdateDialogOpen = false;
        UpdateDialog = null;
    }

    [RelayCommand]
    public void NavigateAnimeList()
    {
        var animeList = EnsureAnimeListViewModel();
        animeList.RefreshLocalization();
        SetCurrentPage(animeList);
    }

    [RelayCommand]
    public void NavigateSeasonal()
    {
        var animeList = EnsureAnimeListViewModel();
        var seasonal = EnsureSeasonalViewModel();
        var userStore = animeList.AnimeItems
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Status);
        seasonal.UpdateUserList(userStore);
        // Trigger the initial Shikimori/MAL load on the very first navigation
        // (no-op on subsequent navigations - the call is idempotent). This
        // replaces an eager preload in SeasonalViewModel's ctor, which used
        // to fire HTTP requests during the app's first render frames.
        seasonal.EnsureInitialLoad();
        SetCurrentPage(seasonal);
    }

    [RelayCommand]
    public void NavigateHistory()
    {
        var history = EnsureHistoryViewModel();
        history.RefreshHistory().SafeFireAndForget("NavigateHistory");
        SetCurrentPage(history);
    }

    [RelayCommand]
    public void NavigateTorrents()
    {
        var torrents = EnsureTorrentsViewModel();
        torrents.RefreshWatchingList();
        SetCurrentPage(torrents);
    }

    [RelayCommand]
    public void NavigateSearch()
    {
        SetCurrentPage(_viewModelFactory.Create<SearchViewModel>());
    }

    [RelayCommand]
    public void NavigateAnalytics()
    {
        if (IsNavigationBlocked) return;
        SelectedNavigationIndex = 0;
        IsSettingsSelected = false;
        IsSettingsOpen = false;
        var analytics = EnsureAnalyticsViewModel();
        analytics.Refresh().SafeFireAndForget("NavigateAnalytics");
        SetCurrentPage(analytics);
    }

    [RelayCommand]
    public void TestPlayer()
    {
        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(assemblyPath) || string.IsNullOrEmpty(processPath)) return;

        var isDotnet = System.IO.Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = processPath,
            Arguments = isDotnet ? $"\"{assemblyPath}\" --player" : "--player",
            UseShellExecute = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
        };

        try
        {
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to launch player process");
        }
    }
}
