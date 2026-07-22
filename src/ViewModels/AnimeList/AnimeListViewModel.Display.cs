using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    public void EnqueueItemForViewport(AnimeItem item)
    {
        if (item == null) return;
        _queueService.EnqueueForViewport(new[] { item });
    }

    public void RefreshAfterDetailsEdit()
    {
        RebuildListProjection();
        UpdateCountsAsync().SafeFireAndForget("RefreshAfterDetailsEdit");
        ApplyCurrentFiltersAsync().SafeFireAndForget("RefreshAfterDetailsEdit");
    }

    private void RebuildListProjection()
    {
        _listProjection.Rebuild(_animeRepo.Collection);
    }

    [RelayCommand]
    public async Task IncrementProgress(AnimeItem item)
    {
        if (item.TotalEpisodes == 0 || item.Progress < item.TotalEpisodes)
        {
            await SetProgressTo(item, item.Progress + 1);
        }
    }

    public async Task SetProgressTo(AnimeItem item, int nextProgress)
    {
        var oldStatus = item.Status;
        var oldRewatching = item.IsRewatching;

        await _progressService.SmartIncrementProgressAsync(item, nextProgress);

        await UpdateCountsAsync();

        // Only re-filter if the status changed (item needs to move to another tab)
        if (item.Status != oldStatus || item.IsRewatching != oldRewatching)
        {
            await ApplyCurrentFiltersAsync();
        }
    }

    [RelayCommand]
    public async Task DecrementProgress(AnimeItem item)
    {
        await _progressService.SmartDecrementProgressAsync(item);
        await UpdateCountsAsync();
    }

    [RelayCommand]
    public void OpenScoreMenu(AnimeItem item) => ActiveItem = item;

    [RelayCommand]
    public async Task ApplyScoreFromMenu(RatingOption rating)
    {
        if (ActiveItem == null || rating == null) return;
        int.TryParse(rating.Value, out int score);
        await _progressService.SetScoreAsync(ActiveItem, score);
    }

    [RelayCommand]
    public async Task SetScore(AnimeItem item)
    {
        if (item == null) return;
        int.TryParse(item.Score, out int score);
        await _progressService.SetScoreAsync(item, score);
    }
}
