using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;

namespace Kiriha.ViewModels;

public enum TorrentSortMode
{
    Newest,
    Matched,
    ReleaseGroup,
}

public sealed class HideableAnimeItem : ObservableObject
{
    public HideableAnimeItem(AnimeItem anime, bool isHidden)
    {
        Anime = anime;
        _isHidden = isHidden;
    }

    public AnimeItem Anime { get; }

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set
        {
            if (SetProperty(ref _isHidden, value))
                HiddenChanged?.Invoke(this);
        }
    }

    public event System.Action<HideableAnimeItem>? HiddenChanged;
}

public sealed class TorrentGroup
{
    public TorrentGroup(string animeTitle, IReadOnlyList<TorrentItem> items)
    {
        AnimeTitle = animeTitle;
        Items = items;
        HasMatch = items.Any(i => i.IsMatched);
        LatestDate = items.Count > 0 ? items.Max(i => i.PublishDate) : System.DateTime.MinValue;
    }

    public string AnimeTitle { get; }
    public IReadOnlyList<TorrentItem> Items { get; }
    public int Count => Items.Count;
    public bool HasMatch { get; }
    public System.DateTime LatestDate { get; }
}
