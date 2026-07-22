using System;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel : ViewModelBase, IDisposable
{
    private readonly MalApiService _apiService;
    private readonly SettingsService _settingsService;
    private readonly LoadQueueService _queueService;
    private readonly AnimeRepository _animeRepo;
    private readonly SeasonalCacheStore _cacheStore;
    private readonly SyncManager _syncManager;
    private readonly Kiriha.Core.Dialogs.IDialogService _dialogService;

    public Kiriha.Core.Dialogs.IDialogService DialogService => _dialogService;

    public SeasonalViewModel(
        MalApiService apiService,
        SettingsService settingsService,
        LoadQueueService queueService,
        AnimeRepository animeRepo,
        SeasonalCacheStore cacheStore,
        SyncManager syncManager,
        Kiriha.Core.Dialogs.IDialogService dialogService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _queueService = queueService;
        _animeRepo = animeRepo;
        _cacheStore = cacheStore;
        _syncManager = syncManager;
        _dialogService = dialogService;

        HydrateDiskCacheOnce();
        LoadSettingsState();
        SetCurrentSeasonFromClock();

        _filterDebouncer = CreateSettingsDebouncer();
        _applyFilterDebouncer = new Kiriha.Utils.Async.Debouncer(TimeSpan.FromMilliseconds(300), () =>
        {
            ApplyFiltersAsync().SafeFireAndForget("ApplyFiltersAsync");
        });

        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var vm = (SeasonalViewModel)r;
                var userStore = vm._animeRepo.Collection
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First().Status);
                vm.UpdateUserList(userStore);
            });
        });

        RefreshLocalization();
        _isInitializing = false;
        ScheduleDeferredInitialLoad();
    }
}
