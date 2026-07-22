using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.AnimeList;

public partial class AnimeListViewModel
{
    [RelayCommand]
    public async Task SyncMal()
    {
        IsBusy = true;
        try
        {
            bool success = SelectedMediaKind == MediaKind.Manga || SelectedMediaKind == MediaKind.LightNovel
                ? await _syncOrchestrator.SyncMangaWithTrackersAsync()
                : await _syncOrchestrator.SyncWithTrackersAsync();

            if (success)
            {
                RebuildListProjection();
                await UpdateCountsAsync();
                await ApplyCurrentFiltersAsync();

                await _airingInfoService.SyncOngoingEpisodesAsync(force: true);
                await _rssService.CheckFeedsAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Manual sync failed");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
