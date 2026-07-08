using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
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
using System;
using System.Collections.Generic;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Seasonal;

#pragma warning disable MVVMTK0034

public partial class SeasonalViewModel
{
    private void LoadSettingsState()
    {
        var settings = _settingsService.Current.UI;
        _sortBy = settings.SeasonalSortBy;
        _filterNsfw = settings.ShowNsfw;
        _showHidden = settings.SeasonalShowHidden;
        _hiddenSeasonalIds = new HashSet<int>(settings.HiddenSeasonalIds ?? new List<int>());
        _filterNotInList = settings.SeasonalStatusFilters.Contains("NotInList");
        _filterWatching = settings.SeasonalStatusFilters.Contains("Watching");
        _filterCompleted = settings.SeasonalStatusFilters.Contains("Completed");
        _filterOnHold = settings.SeasonalStatusFilters.Contains("OnHold");
        _filterPlanToWatch = settings.SeasonalStatusFilters.Contains("PlanToWatch");
        _filterDropped = settings.SeasonalStatusFilters.Contains("Dropped");
    }

    private void SetCurrentSeasonFromClock()
    {
        int month = DateTime.Now.Month;
        _currentYear = DateTime.Now.Year;
        if (month == 12) _currentYear++;

        _currentSeason = month switch
        {
            1 or 2 or 12 => Constants.Seasons.Winter,
            3 or 4 or 5 => Constants.Seasons.Spring,
            6 or 7 or 8 => Constants.Seasons.Summer,
            _ => Constants.Seasons.Fall
        };
    }

    private Kiriha.Utils.Async.Debouncer CreateSettingsDebouncer() =>
        new(TimeSpan.FromMilliseconds(500), async (ct) =>
        {
            _settingsService.Update(settings =>
            {
                var s = settings.UI;
                s.SeasonalSortBy = SortBy;
                s.ShowNsfw = FilterNsfw;
                s.SeasonalShowHidden = ShowHidden;

                s.SeasonalStatusFilters.Clear();
                if (FilterNotInList) s.SeasonalStatusFilters.Add("NotInList");
                if (FilterWatching) s.SeasonalStatusFilters.Add("Watching");
                if (FilterCompleted) s.SeasonalStatusFilters.Add("Completed");
                if (FilterOnHold) s.SeasonalStatusFilters.Add("OnHold");
                if (FilterPlanToWatch) s.SeasonalStatusFilters.Add("PlanToWatch");
                if (FilterDropped) s.SeasonalStatusFilters.Add("Dropped");
            }, SettingsSection.UI, save: false);

            await _settingsService.SaveAsync();
        });

    private void SaveSettingsDebounced()
    {
        if (_isInitializing) return;
        _filterDebouncer.Invoke();
    }
}

#pragma warning restore MVVMTK0034
