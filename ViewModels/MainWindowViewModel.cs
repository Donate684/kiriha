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

    public SettingsViewModel SettingsViewModel => _settingsViewModel;

    private readonly AnimeListViewModel _animeListViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly NowPlayingViewModel _nowPlayingViewModel;
    private readonly HistoryViewModel _historyViewModel;
    private readonly TorrentsViewModel _torrentsViewModel;
    private readonly SeasonalViewModel _seasonalViewModel;
    private readonly AnalyticsViewModel _analyticsViewModel;
    
    // IViewModelFactory delivers a fresh transient instance on each navigation Ã¢â‚¬â€
    // see DI registrations: WelcomeViewModel and SearchViewModel are AddTransient.
    private readonly IViewModelFactory _viewModelFactory;
    
    private readonly Kiriha.Services.Data.SettingsService _settingsService;
    private readonly Kiriha.Services.UpdateService _updateService;

    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    public MainWindowViewModel(
        AnimeListViewModel animeListVm, 
        SettingsViewModel settingsVm, 
        NowPlayingViewModel nowPlayingVm, 
        HistoryViewModel historyVm, 
        TorrentsViewModel torrentsVm,
        SeasonalViewModel seasonalVm,
        AnalyticsViewModel analyticsVm,
        IViewModelFactory viewModelFactory,
        Kiriha.Services.Data.SettingsService settingsService,
        Kiriha.Services.UpdateService updateService)
    {
        _animeListViewModel = animeListVm;
        _settingsViewModel = settingsVm;
        _nowPlayingViewModel = nowPlayingVm;
        _historyViewModel = historyVm;
        _torrentsViewModel = torrentsVm;
        _seasonalViewModel = seasonalVm;
        _analyticsViewModel = analyticsVm;
        _viewModelFactory = viewModelFactory;
        _settingsService = settingsService;
        _updateService = updateService;

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
        _settingsService.Update(settings => settings.UI.IsPaneOpen = value);
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
            CurrentPage != _settingsViewModel &&
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
        SetCurrentPage(_nowPlayingViewModel);
    }

    [RelayCommand]
    public void NavigateSettings()
    {
        if (IsNavigationBlocked) return;
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
        UpdateDialog = new UpdateDialogViewModel(_updateService, CloseUpdateDialog, isDownloaded);
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
        _animeListViewModel.RefreshLocalization();
        SetCurrentPage(_animeListViewModel);
    }

    [RelayCommand]
    public void NavigateSeasonal()
    {
        var userStore = _animeListViewModel.AnimeItems
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Status);
        _seasonalViewModel.UpdateUserList(userStore);
        // Trigger the initial Shikimori/MAL load on the very first navigation
        // (no-op on subsequent navigations - the call is idempotent). This
        // replaces an eager preload in SeasonalViewModel's ctor, which used
        // to fire HTTP requests during the app's first render frames.
        _seasonalViewModel.EnsureInitialLoad();
        SetCurrentPage(_seasonalViewModel);
    }

    [RelayCommand]
    public void NavigateHistory()
    {
        _historyViewModel.RefreshHistory().SafeFireAndForget("NavigateHistory");
        SetCurrentPage(_historyViewModel);
    }

    [RelayCommand]
    public void NavigateTorrents()
    {
        _torrentsViewModel.RefreshWatchingList();
        SetCurrentPage(_torrentsViewModel);
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
        _analyticsViewModel.Refresh().SafeFireAndForget("NavigateAnalytics");
        SetCurrentPage(_analyticsViewModel);
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
