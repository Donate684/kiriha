using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels;

public partial class SeasonalViewModel
{
    public void ApplyFilters() => _applyFilterDebouncer.Invoke();

    private async Task ApplyFiltersAsync()
    {
        if (_isInitializing) return;

        int requestId = Interlocked.Increment(ref _applyFiltersRequestCount);
        _queueService.ClearQueues();

        var userStore = _userAnimeStore;
        var request = new SeasonalFilterRequest(
            Items: _allSeasonalItems ?? new List<AnimeItem>(),
            UserStore: userStore,
            SearchQuery: SearchQuery,
            SortBy: SortBy,
            FilterNsfw: FilterNsfw,
            SelectedCategory: SelectedCategory,
            CurrentYear: CurrentYear,
            CurrentSeason: CurrentSeason,
            HiddenIds: _hiddenSeasonalIds.Count == 0 ? new HashSet<int>() : new HashSet<int>(_hiddenSeasonalIds),
            ShowHidden: ShowHidden,
            FilterNotInList: FilterNotInList,
            FilterWatching: FilterWatching,
            FilterCompleted: FilterCompleted,
            FilterOnHold: FilterOnHold,
            FilterPlanToWatch: FilterPlanToWatch,
            FilterDropped: FilterDropped);

        var result = await Task.Run(() => SeasonalFilterEngine.Apply(request));
        if (requestId != _applyFiltersRequestCount) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (requestId != _applyFiltersRequestCount) return;
            ApplyFilterResult(result, userStore);
            SaveSettingsDebounced();
        });
    }

    private void ApplyFilterResult(SeasonalFilterResult result, Dictionary<int, UserAnimeStatus> userStore)
    {
        foreach (var item in result.Items)
        {
            item.Status = userStore.TryGetValue(item.Id, out var status) ? status : UserAnimeStatus.None;
            item.IsHiddenInSeasons = _hiddenSeasonalIds.Contains(item.Id);
        }

        if (SelectedCategory != result.ResolvedCategory)
        {
            SelectedCategory = result.ResolvedCategory;
        }

        if (!IsSameDisplayList(result.Items))
        {
            DisplayItems = new AvaloniaList<AnimeItem>(result.Items);
        }

        CurrentHeader = result.Header;
        NewHeader = result.Headers["New"];
        ContinuingHeader = result.Headers["Continuing"];
        MoviesHeader = result.Headers["Movies"];
        OvaHeader = result.Headers["OVA"];
        OnaHeader = result.Headers["ONA"];
        SpecialsHeader = result.Headers["Specials"];
        OtherHeader = result.Headers["Other"];

        IsFilterActive = FilterNotInList || FilterWatching || FilterCompleted || FilterOnHold ||
                         FilterPlanToWatch || FilterDropped || FilterNsfw || ShowHidden ||
                         !string.IsNullOrEmpty(SearchQuery);
    }

    private bool IsSameDisplayList(IReadOnlyList<AnimeItem> items)
    {
        if (DisplayItems.Count != items.Count) return false;
        for (int i = 0; i < items.Count; i++)
        {
            if (DisplayItems[i].Id != items[i].Id) return false;
        }
        return true;
    }

    [RelayCommand]
    public async Task SelectCategory(string category)
    {
        if (SelectedCategory == category) return;
        SelectedCategory = category;
        await ApplyFiltersAsync();
    }

    [RelayCommand]
    public void ClearFilters()
    {
        FilterNotInList = false;
        FilterWatching = false;
        FilterCompleted = false;
        FilterOnHold = false;
        FilterPlanToWatch = false;
        FilterDropped = false;
        FilterNsfw = false;
        ShowHidden = false;
    }

    partial void OnSearchQueryChanged(string? value) => ApplyFilters();
    partial void OnSortByChanged(string value) => ApplyFilters();
    partial void OnFilterNotInListChanged(bool value) => ApplyFilters();
    partial void OnFilterWatchingChanged(bool value) => ApplyFilters();
    partial void OnFilterCompletedChanged(bool value) => ApplyFilters();
    partial void OnFilterOnHoldChanged(bool value) => ApplyFilters();
    partial void OnFilterPlanToWatchChanged(bool value) => ApplyFilters();
    partial void OnFilterDroppedChanged(bool value) => ApplyFilters();
    partial void OnFilterNsfwChanged(bool value) => ApplyFilters();
    partial void OnShowHiddenChanged(bool value) => ApplyFilters();
}
