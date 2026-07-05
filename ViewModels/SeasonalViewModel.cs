using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;

namespace Kiriha.ViewModels;

public partial class SeasonalViewModel : ViewModelBase, IDisposable
{
    private readonly MalApiService _apiService;
    private readonly SettingsService _settingsService;
    private readonly LoadQueueService _queueService;
    private readonly AnimeService _animeService;
    private readonly SeasonalCacheStore _cacheStore;
    private readonly SyncManager _syncManager;

    public SeasonalViewModel(
        MalApiService apiService,
        SettingsService settingsService,
        LoadQueueService queueService,
        AnimeService animeService,
        SeasonalCacheStore cacheStore,
        SyncManager syncManager)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _queueService = queueService;
        _animeService = animeService;
        _cacheStore = cacheStore;
        _syncManager = syncManager;

        HydrateDiskCacheOnce();
        LoadSettingsState();
        SetCurrentSeasonFromClock();

        _filterDebouncer = CreateSettingsDebouncer();
        _applyFilterDebouncer = new Utils.Debouncer(TimeSpan.FromMilliseconds(300), () =>
        {
            ApplyFiltersAsync().SafeFireAndForget("ApplyFiltersAsync");
        });

        WeakReferenceMessenger.Default.Register<AnimeListRefreshMessage>(this, (r, m) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var vm = (SeasonalViewModel)r;
                var userStore = vm._animeService.Collection
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
