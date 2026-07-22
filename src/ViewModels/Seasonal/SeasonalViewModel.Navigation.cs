using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Utils.Async;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplaySeason));
        OnPropertyChanged(nameof(DisplaySortBy));
    }

    public void EnqueueItemForViewport(AnimeItem item)
    {
        if (item == null) return;
        _queueService.EnqueueForViewport(new[] { item });
    }

    [RelayCommand]
    public void NextSeason()
    {
        int idx = Seasons.IndexOf(CurrentSeason);
        if (idx == 3) { CurrentSeason = Seasons[0]; CurrentYear++; }
        else { CurrentSeason = Seasons[idx + 1]; }
    }

    [RelayCommand]
    public void PreviousSeason()
    {
        int idx = Seasons.IndexOf(CurrentSeason);
        if (idx == 0) { CurrentSeason = Seasons[3]; CurrentYear--; }
        else { CurrentSeason = Seasons[idx - 1]; }
    }

    partial void OnCurrentYearChanged(int value)
    {
        if (_isInitializing) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }

    partial void OnCurrentSeasonChanged(string value)
    {
        if (_isInitializing) return;
        LoadSeasonalAnimeAsync().SafeFireAndForget("LoadSeasonalAnimeAsync");
    }
}
