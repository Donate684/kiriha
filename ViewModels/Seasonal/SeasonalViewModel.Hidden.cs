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
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    public void UpdateUserList(Dictionary<int, UserAnimeStatus> userList)
    {
        _userAnimeStore = userList;
        UnhideTrackedTitles(userList);
        ApplyFilters();
    }

    private void UnhideTrackedTitles(Dictionary<int, UserAnimeStatus> userList)
    {
        if (_hiddenSeasonalIds.Count == 0) return;

        List<int>? toUnhide = null;
        foreach (var id in _hiddenSeasonalIds)
        {
            if (userList.TryGetValue(id, out var status) && status != UserAnimeStatus.None)
                (toUnhide ??= new List<int>()).Add(id);
        }

        if (toUnhide == null) return;

        foreach (var id in toUnhide)
        {
            _hiddenSeasonalIds.Remove(id);
        }

        _settingsService.Update(settings =>
        {
            foreach (var id in toUnhide)
                settings.UI.HiddenSeasonalIds?.Remove(id);
        }, SettingsSection.UI, save: false);
        _ = _settingsService.SaveAsync();
    }

    [RelayCommand]
    public void ToggleHiddenSeasonal(AnimeItem? item)
    {
        if (item == null) return;

        bool isHidden = _hiddenSeasonalIds.Contains(item.Id);
        if (!isHidden && item.Status != UserAnimeStatus.None) return;

        _settingsService.Update(settings =>
        {
            var list = settings.UI.HiddenSeasonalIds ??= new List<int>();
            if (isHidden)
            {
                _hiddenSeasonalIds.Remove(item.Id);
                list.Remove(item.Id);
                item.IsHiddenInSeasons = false;
            }
            else
            {
                _hiddenSeasonalIds.Add(item.Id);
                if (!list.Contains(item.Id)) list.Add(item.Id);
                item.IsHiddenInSeasons = true;
            }
        }, SettingsSection.UI, save: false);
        _ = _settingsService.SaveAsync();
        ApplyFilters();
    }
}
