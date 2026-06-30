using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Models;

namespace Kiriha.Core;

/// <summary>
/// Centralized engine for filtering and sorting AnimeItem collections.
/// </summary>
public static class AnimeFilterEngine
{
    public static IEnumerable<AnimeItem> ApplySearch(this IEnumerable<AnimeItem> query, string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery)) return query;

        return query.Where(x =>
            (x.Title?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (x.RussianTitle?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (x.EnglishTitle?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true) ||
            (x.JapaneseTitle?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true));
    }

    /// <summary>
    /// Filters the collection to show ONLY NSFW content if filterNsfw is true.
    /// This is an "Only NSFW 18+" mode, not a "Hide NSFW" filter.
    /// </summary>
    public static IEnumerable<AnimeItem> ApplyNsfw(this IEnumerable<AnimeItem> query, bool filterNsfw)
    {
        if (filterNsfw)
        {
            return query.Where(x =>
                string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Nsfw, "black", StringComparison.OrdinalIgnoreCase) ||
                x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
        }

        return query.Where(x =>
            !string.Equals(x.Rating, "rx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(x.Nsfw, "black", StringComparison.OrdinalIgnoreCase) &&
            !x.Genres.Any(g => string.Equals(g, "Hentai", StringComparison.OrdinalIgnoreCase)));
    }

    public static IEnumerable<AnimeItem> ApplySorting(this IEnumerable<AnimeItem> query, string? sortBy, bool isSeasonal = false)
    {
        return sortBy switch
        {
            "Score" => query.OrderByDescending(x => {
                if (isSeasonal)
                {
                    // Strictly community mean score for seasons
                    if (double.TryParse(x.MeanScore?.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var m))
                        return m;
                    return 0.0;
                }

                // Strictly user score for personal list
                if (double.TryParse(x.Score?.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s))
                    return s;
                return 0.0;
            }),
            "Progress" => query.OrderByDescending(x => x.ProgressValue),
            "Date" => query.OrderByDescending(x => x.AiringDate ?? DateTime.MinValue),
            "Popularity" => query.OrderBy(x => x.Popularity <= 0 ? int.MaxValue : x.Popularity),
            "EnglishTitle" => query.OrderBy(x => !string.IsNullOrEmpty(x.EnglishTitle) ? x.EnglishTitle : x.Title),
            "RussianTitle" => query.OrderBy(x => !string.IsNullOrEmpty(x.RussianTitle) ? x.RussianTitle : x.Title),
            "Title" => query.OrderBy(x => x.Title),
            _ => query.OrderBy(x => x.Title)
        };
    }
}
