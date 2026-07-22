using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    [RelayCommand]
    public void ClearFilters()
    {
        FilterNsfw = false;
        IsFilterActive = false;
    }

    [RelayCommand]
    public async Task SwitchMediaKind(string kindString)
    {
        if (Enum.TryParse<MediaKind>(kindString, true, out var kind))
        {
            SelectedMediaKind = kind;
            await UpdateCountsAsync();
            await ApplyCurrentFiltersAsync();
        }
    }

    [RelayCommand]
    public async Task ToggleMediaKind()
    {
        SelectedMediaKind = SelectedMediaKind == MediaKind.Anime ? MediaKind.Manga : MediaKind.Anime;
        await UpdateCountsAsync();
        await ApplyCurrentFiltersAsync();
    }

    [RelayCommand]
    public async Task Filter(string statusString)
    {
        if (Enum.TryParse<UserAnimeStatus>(statusString, true, out var parsed))
        {
            await FilterByStatusAsync(parsed);
        }
    }

    public async Task FilterByStatusAsync(UserAnimeStatus status)
    {
        SelectedStatus = status;
        await ApplyCurrentFiltersAsync();
    }

    private void ScheduleFilterRefresh()
    {
        _filterRefreshDebouncer?.Invoke();
    }

    private async Task ApplyCurrentFiltersAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _filterRefreshVersion);
        var status = SelectedStatus;
        var query = SearchQuery;
        var nsfw = FilterNsfw;
        var sort = SortBy;
        var kind = SelectedMediaKind;

        var filtered = await Dispatcher.UIThread.InvokeAsync(() =>
            _listProjection.Query(status, query, nsfw, sort, kind));

        if (cancellationToken.IsCancellationRequested || version != Volatile.Read(ref _filterRefreshVersion))
            return;

        // In-place update of the existing AvaloniaList instead of replacing the
        // reference. Re-assigning FilteredItems would force every binding (and
        // any ItemsRepeater materializing this collection) to detach + rebind
        // the entire visual tree, which is what caused a ~358 ms UI-thread
        // stall right after startup population. Mutating the same instance
        // emits one CollectionChanged(Reset) and lets ItemsRepeater diff in
        // its own incremental pipeline.
        FilteredItems.Clear();
        FilteredItems.AddRange(filtered);
    }

    private async Task UpdateCountsAsync()
    {
        var kind = SelectedMediaKind;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var watching = _listProjection.Count(UserAnimeStatus.Watching, kind);
            var completed = _listProjection.Count(UserAnimeStatus.Completed, kind);
            var onHold = _listProjection.Count(UserAnimeStatus.OnHold, kind);
            var dropped = _listProjection.Count(UserAnimeStatus.Dropped, kind);
            var ptw = _listProjection.Count(UserAnimeStatus.PlanToWatch, kind);

            WatchingHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.watching"), watching.ToString());
            CompletedHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.completed"), completed.ToString());
            OnHoldHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.on_hold"), onHold.ToString());
            DroppedHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.dropped"), dropped.ToString());
            PlanToWatchHeader = UIUtils.GetLoc("filters.header_format", GetLoc("anime.status.plan_to_watch"), ptw.ToString());
        });
    }
}
