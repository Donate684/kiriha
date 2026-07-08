using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;

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
