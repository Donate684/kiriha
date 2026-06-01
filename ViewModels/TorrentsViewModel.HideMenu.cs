using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels;

public partial class TorrentsViewModel
{
    [RelayCommand]
    public void ToggleHideMode() => IsHideMode = !IsHideMode;

    [RelayCommand]
    public void ToggleHideAnime(HideableAnimeItem? item)
    {
        if (item == null) return;
        item.IsHidden = !item.IsHidden;
    }

    public void RefreshWatchingList()
    {
        var hidden = new HashSet<int>(_settingsService.Current.Torrents.HiddenAnimeIds ?? new List<int>());
        var watching = _animeService.Collection.Where(x => x.Status == UserAnimeStatus.Watching).ToList();

        WatchingAnime.Clear();
        foreach (var a in watching)
            if (!hidden.Contains(a.Id)) WatchingAnime.Add(a);

        HideMenuItems.Clear();
        foreach (var a in watching)
        {
            var item = new HideableAnimeItem(a, hidden.Contains(a.Id));
            item.HiddenChanged += OnHideMenuItemChanged;
            HideMenuItems.Add(item);
        }
    }

    private void OnHideMenuItemChanged(HideableAnimeItem item)
    {
        _settingsService.Update(settings =>
        {
            var ids = settings.Torrents.HiddenAnimeIds ??= new List<int>();
            if (item.IsHidden)
            {
                if (!ids.Contains(item.Anime.Id)) ids.Add(item.Anime.Id);
            }
            else
            {
                ids.Remove(item.Anime.Id);
            }
        });

        if (item.IsHidden)
        {
            var existing = WatchingAnime.FirstOrDefault(a => a.Id == item.Anime.Id);
            if (existing != null) WatchingAnime.Remove(existing);
        }
        else if (!WatchingAnime.Any(a => a.Id == item.Anime.Id))
        {
            WatchingAnime.Add(item.Anime);
        }
    }
}
