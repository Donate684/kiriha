using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Core;

public sealed class AnimeCollectionProjection : IDisposable
{
    private readonly Dictionary<int, Entry> _entriesById = new();
    private readonly Dictionary<UserAnimeStatus, Dictionary<int, Entry>> _buckets = new()
    {
        [UserAnimeStatus.Watching] = new(),
        [UserAnimeStatus.Completed] = new(),
        [UserAnimeStatus.OnHold] = new(),
        [UserAnimeStatus.Dropped] = new(),
        [UserAnimeStatus.PlanToWatch] = new(),
    };
    private readonly Dictionary<UserAnimeStatus, Dictionary<MediaKind, int>> _counts = new()
    {
        [UserAnimeStatus.Watching] = new(),
        [UserAnimeStatus.Completed] = new(),
        [UserAnimeStatus.OnHold] = new(),
        [UserAnimeStatus.Dropped] = new(),
        [UserAnimeStatus.PlanToWatch] = new(),
    };

    public void Rebuild(IEnumerable<AnimeItem> items)
    {
        Clear();

        foreach (var item in items)
        {
            Add(item);
        }
    }

    public void ApplyCollectionChange(NotifyCollectionChangedEventArgs e, IEnumerable<AnimeItem> currentItems)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AddItems(e.NewItems?.OfType<AnimeItem>());
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveItems(e.OldItems?.OfType<AnimeItem>());
                break;
            case NotifyCollectionChangedAction.Replace:
                RemoveItems(e.OldItems?.OfType<AnimeItem>());
                AddItems(e.NewItems?.OfType<AnimeItem>());
                break;
            case NotifyCollectionChangedAction.Move:
                break;
            default:
                Rebuild(currentItems);
                break;
        }
    }

    public int Count(UserAnimeStatus status, MediaKind kind)
    {
        return _counts.TryGetValue(status, out var countsByKind) && countsByKind.TryGetValue(kind, out var count)
            ? count
            : 0;
    }

    public List<AnimeItem> Query(UserAnimeStatus status, string? searchQuery, bool filterNsfw, string? sortBy, MediaKind kind)
    {
        if (!_buckets.TryGetValue(status, out var bucket))
        {
            return new List<AnimeItem>();
        }

        var normalizedSearch = Normalize(searchQuery);
        var query = bucket.Values.Where(x => x.Kind == kind);

        if (normalizedSearch.Length > 0)
        {
            query = query.Where(x => x.SearchableText.Contains(normalizedSearch, StringComparison.Ordinal));
        }

        query = filterNsfw
            ? query.Where(x => x.IsNsfw)
            : query.Where(x => !x.IsNsfw);

        return query.Select(x => x.Item).ApplySorting(sortBy).ToList();
    }

    public void Dispose()
    {
        Clear();
    }

    private void AddItems(IEnumerable<AnimeItem>? items)
    {
        if (items == null) return;
        foreach (var item in items) Add(item);
    }

    private void RemoveItems(IEnumerable<AnimeItem>? items)
    {
        if (items == null) return;
        foreach (var item in items) Remove(item);
    }

    private void Add(AnimeItem item)
    {
        Remove(item);

        var entry = Entry.From(item);
        _entriesById[item.Id] = entry;
        if (_buckets.TryGetValue(entry.ListStatus, out var bucket))
        {
            bucket[item.Id] = entry;

            if (!_counts[entry.ListStatus].TryGetValue(entry.Kind, out var count))
            {
                count = 0;
            }
            _counts[entry.ListStatus][entry.Kind] = count + 1;
        }

        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void Remove(AnimeItem item)
    {
        if (!_entriesById.Remove(item.Id, out var entry)) return;

        if (_buckets.TryGetValue(entry.ListStatus, out var bucket))
        {
            bucket.Remove(item.Id);

            if (_counts[entry.ListStatus].TryGetValue(entry.Kind, out var count))
            {
                _counts[entry.ListStatus][entry.Kind] = count - 1;
            }
        }

        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void Clear()
    {
        foreach (var entry in _entriesById.Values)
        {
            entry.Item.PropertyChanged -= OnItemPropertyChanged;
        }

        _entriesById.Clear();
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
        }
        foreach (var countBucket in _counts.Values)
        {
            countBucket.Clear();
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AnimeItem item) return;

        if (string.IsNullOrEmpty(e.PropertyName) || AffectsProjection(e.PropertyName))
        {
            Add(item);
        }
    }

    private static bool AffectsProjection(string propertyName)
    {
        return propertyName is nameof(AnimeItem.Title)
            or nameof(AnimeItem.RussianTitle)
            or nameof(AnimeItem.EnglishTitle)
            or nameof(AnimeItem.JapaneseTitle)
            or nameof(AnimeItem.Rating)
            or nameof(AnimeItem.Status)
            or nameof(AnimeItem.IsRewatching)
            or nameof(AnimeItem.MediaKind);
    }

    private static UserAnimeStatus GetListStatus(AnimeItem item)
    {
        return item.Status == UserAnimeStatus.Watching || item.IsRewatching
            ? UserAnimeStatus.Watching
            : item.Status;
    }

    private static string BuildSearchableText(AnimeItem item)
    {
        return Normalize(string.Join('\n',
        [
            item.Title,
            item.RussianTitle,
            item.EnglishTitle,
            item.JapaneseTitle
        ]));
    }

    private static bool ComputeIsNsfw(AnimeItem item)
    {
        return string.Equals(item.Rating, "rx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Nsfw, "black", StringComparison.OrdinalIgnoreCase)
            || item.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.ToUpperInvariant();
    }

    private sealed record Entry(
        AnimeItem Item,
        UserAnimeStatus ListStatus,
        string SearchableText,
        bool IsNsfw,
        MediaKind Kind)
    {
        public static Entry From(AnimeItem item)
        {
            return new Entry(item, GetListStatus(item), BuildSearchableText(item), ComputeIsNsfw(item), item.MediaKind);
        }
    }
}
