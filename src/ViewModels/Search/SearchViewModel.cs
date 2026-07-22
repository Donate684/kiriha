using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Utils.Collections;
using Serilog;

namespace Kiriha.ViewModels.Search;

public partial class SearchViewModel : ViewModelBase, IDisposable
{
    private readonly MalApiService _apiService;
    private readonly ShikiMetadataService _shikiMetadataService;
    private readonly SettingsService _settingsService;
    private readonly LoadQueueService _queueService;
    private readonly AnimeRepository _animeRepo;
    private readonly SyncManager _syncManager;
    private readonly Kiriha.Core.Dialogs.IDialogService _dialogService;

    public Kiriha.Core.Dialogs.IDialogService DialogService => _dialogService;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hideInLists;
    [NotifyPropertyChangedFor(nameof(DisplayAdultFilter))]
    [ObservableProperty] private AdultFilterMode _adultFilter = AdultFilterMode.Hide;

    public AdultFilterMode[] AdultFilterOptions { get; } = [AdultFilterMode.Hide, AdultFilterMode.Include, AdultFilterMode.Only];
    public string DisplayAdultFilter => AdultFilter switch
    {
        AdultFilterMode.Hide => UIUtils.GetLoc("filters.adult.hide"),
        AdultFilterMode.Include => UIUtils.GetLoc("filters.adult.include"),
        AdultFilterMode.Only => UIUtils.GetLoc("filters.adult.only"),
        _ => "18+"
    };

    [RelayCommand]
    public void CycleAdultFilter()
    {
        AdultFilter = AdultFilter switch
        {
            AdultFilterMode.Hide => AdultFilterMode.Include,
            AdultFilterMode.Include => AdultFilterMode.Only,
            AdultFilterMode.Only => AdultFilterMode.Hide,
            _ => AdultFilterMode.Hide
        };
    }

    public BulkObservableCollection<AnimeItem> SearchResults { get; } = new();

    private CancellationTokenSource? _searchCts;
    private bool _isDisposed;
    private readonly Kiriha.Utils.Async.Debouncer _searchDebouncer;

    public SearchViewModel(MalApiService apiService, ShikiMetadataService shikiMetadataService,
        SettingsService settingsService, LoadQueueService queueService,
        AnimeRepository animeRepo, SyncManager syncManager, Kiriha.Core.Dialogs.IDialogService dialogService)
    {
        _apiService = apiService;
        _shikiMetadataService = shikiMetadataService;
        _settingsService = settingsService;
        _queueService = queueService;
        _animeRepo = animeRepo;
        _syncManager = syncManager;
        _dialogService = dialogService;

        _searchDebouncer = new Kiriha.Utils.Async.Debouncer(TimeSpan.FromMilliseconds(800), _ =>
        {
            return Dispatcher.UIThread.InvokeAsync(() => PerformSearch());
        });
    }

    /// <summary>
    /// Called from view's ElementPrepared handler to lazily load images
    /// only for items that have entered the viewport.
    /// </summary>
    public void EnqueueItemForViewport(AnimeItem item)
    {
        if (item == null) return;
        _queueService.EnqueueForViewport(new[] { item });
    }

    partial void OnSearchQueryChanged(string value) => TriggerSearch();
    partial void OnHideInListsChanged(bool value) => TriggerSearch();
    partial void OnAdultFilterChanged(AdultFilterMode value) => TriggerSearch();

    private void TriggerSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SearchQuery.Length < 3)
        {
            var cts = Interlocked.Exchange(ref _searchCts, null);
            if (cts != null)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                cts.Dispose();
            }
            SearchResults.Clear();
            return;
        }
        _searchDebouncer.Invoke();
    }

    [RelayCommand]
    public async Task PerformSearch()
    {
        if (_isDisposed || string.IsNullOrWhiteSpace(SearchQuery)) return;

        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _searchCts, newCts);

        if (oldCts != null)
        {
            try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            oldCts.Dispose();
        }

        var ct = newCts.Token;

        IsLoading = true;
        SearchResults.Clear();

        try
        {
            string actualQuery = SearchQuery;
            if (actualQuery.Any(c => c >= '\u0400' && c <= '\u04FF'))
            {
                var englishName = await _shikiMetadataService.ResolveRussianQueryAsync(actualQuery, ct);
                if (!string.IsNullOrEmpty(englishName))
                {
                    actualQuery = englishName;
                }
            }

            var results = await _apiService.SearchAnimeAsync(actualQuery, ct);
            if (results.Any())
            {
                IEnumerable<AnimeItem> filtered = results;

                // Adult filtering
                if (AdultFilter == AdultFilterMode.Hide)
                {
                    // Strict safe mode: hide Rx rating and Hentai genre
                    filtered = filtered.Where(x =>
                        !string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) &&
                        !x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
                }
                else if (AdultFilter == AdultFilterMode.Only)
                {
                    // Adult only: show only Rx rating or Hentai genre
                    filtered = filtered.Where(x =>
                        string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) ||
                        x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
                }

                // Hide in lists filtering
                if (HideInLists)
                {
                    filtered = filtered.Where(x => x.Status == UserAnimeStatus.None);
                }

                SearchResults.Reset(filtered);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Search failed");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    public async Task AddToWatching(AnimeItem item) => await AddToList(item, UserAnimeStatus.Watching);

    [RelayCommand]
    public async Task AddToPlanToWatch(AnimeItem item) => await AddToList(item, UserAnimeStatus.PlanToWatch);

    private async Task AddToList(AnimeItem item, UserAnimeStatus status)
    {
        IsLoading = true;
        try
        {
            item.Status = status;
            await _animeRepo.AddOrUpdateAnimeAsync(item);
            await _syncManager.EnqueueUpdateAsync(item.Id, 0, status);

            // Notify UI
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add {Title}", item.Title);
        }
        finally { IsLoading = false; }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        var cts = Interlocked.Exchange(ref _searchCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }

        _searchDebouncer?.Dispose();
    }
}
