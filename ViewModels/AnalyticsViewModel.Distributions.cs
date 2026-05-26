using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels;

public partial class AnalyticsViewModel
{
    private void ClearAnalyticsCollections()
    {
        Metrics.Clear();
        StatusDistribution.Clear();
        ScoreDistribution.Clear();
        GenreDistribution.Clear();
        StudioDistribution.Clear();
        YearDistribution.Clear();
        ReleaseYearCompletions.Clear();
        TasteHighlights.Clear();
        FavoriteGenres.Clear();
        FavoriteStudios.Clear();
        WatchTodo.Clear();
        FinishedWatchTodo.Clear();
        UpcomingTodo.Clear();
        PlanTodo.Clear();
        StaleTodo.Clear();
        RecentHistory.Clear();
        MonthlyHistory.Clear();
    }

    private static double EstimateHoursWatched(IEnumerable<AnimeItem> items)
    {
        return items.Sum(item =>
        {
            var episodeMinutes = string.Equals(item.Type, Constants.AnimeTypes.Movie, StringComparison.OrdinalIgnoreCase)
                ? 95
                : 24;
            return Math.Max(0, item.Progress) * episodeMinutes / 60.0;
        });
    }

    private void AddStatusDistribution(IReadOnlyCollection<AnimeItem> items)
    {
        var groups = items
            .GroupBy(x => x.Status)
            .Select(x => new
            {
                Status = x.Key,
                Count = x.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        foreach (var group in groups)
        {
            var percent = items.Count > 0 ? group.Count * 100.0 / items.Count : 0;
            StatusDistribution.Add(new AnalyticsBar
            {
                Label = GetStatusLabel(group.Status),
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = Percent(group.Count, items.Count),
                ShareText = $"{percent:0.#}% списка",
                Accent = GetStatusAccent(group.Status)
            });
        }
    }

    private void AddScoreDistribution(IReadOnlyCollection<int> scores)
    {
        Span<int> counts = stackalloc int[11];
        foreach (var score in scores)
        {
            if (score is >= 1 and <= 10)
            {
                counts[score]++;
            }
        }

        var maxCount = 0;
        for (var score = 1; score <= 10; score++)
        {
            maxCount = Math.Max(maxCount, counts[score]);
        }

        for (var score = 10; score >= 1; score--)
        {
            var count = counts[score];
            ScoreDistribution.Add(new AnalyticsBar
            {
                Label = score.ToString(),
                Value = count.ToString("N0"),
                Count = count,
                Percent = Percent(count, maxCount),
                Accent = GetScoreAccent(score),
                BarHeight = count == 0 ? 0 : 6 + (count / (double)Math.Max(1, maxCount)) * 66
            });
        }
    }

    private static string GetScoreAccent(int score) => score switch
    {
        10 or 9 => "#FF2E9D62",
        8 or 7  => "#FF2D7DD2",
        6 or 5  => "#FFD17A22",
        4 or 3  => "#FFD1495B",
        _       => "#FFE53935"
    };

    private static void AddTopDistribution(ObservableCollection<AnalyticsBar> target, IEnumerable<string> values, int take)
    {
        var groups = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x.Trim())
            .Select(x => new { Label = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Label)
            .Take(take)
            .ToList();

        var max = groups.Count == 0 ? 1 : groups.Max(x => x.Count);
        foreach (var group in groups)
        {
            var share = group.Count * 100.0 / max;
            target.Add(new AnalyticsBar
            {
                Label = group.Label,
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = Percent(group.Count, max),
                ShareText = $"{share:0}%",
                Accent = GetAccent(group.Label)
            });
        }
    }

    private void AddTasteHighlights()
    {
        foreach (var item in GenreDistribution.Take(3))
        {
            TasteHighlights.Add(new AnalyticsBar
            {
                Label = item.Label,
                Value = item.Value,
                Count = item.Count,
                Percent = item.Percent,
                ShareText = item.ShareText,
                Accent = item.Accent
            });
        }
    }

    private void AddYearDistribution(IEnumerable<AnimeItem> completed)
    {
        var groups = completed
            .Where(x => x.StartYear.HasValue)
            .GroupBy(x => x.StartYear!.Value)
            .Select(x => new { Year = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Year)
            .Take(12)
            .OrderBy(x => x.Year)
            .ToList();

        var max = groups.Count == 0 ? 1 : groups.Max(x => x.Count);
        foreach (var group in groups)
        {
            YearDistribution.Add(new AnalyticsBar
            {
                Label = group.Year.ToString(),
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = Percent(group.Count, max)
            });
        }
    }

    private void AddReleaseYearCompletions(IEnumerable<AnimeItem> completed)
    {
        var groups = completed
            .Where(x => x.StartYear.HasValue)
            .GroupBy(x => x.StartYear!.Value)
            .Select(x => new { Year = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Year)
            .ToList();

        var max = groups.Count == 0 ? 1 : groups.Max(x => x.Count);
        foreach (var group in groups)
        {
            var intensity = group.Count / (double)max;
            var alpha = (byte)Math.Round(0x24 + intensity * (0xFF - 0x24));
            ReleaseYearCompletions.Add(new AnalyticsBar
            {
                Label = group.Year.ToString(CultureInfo.InvariantCulture),
                Value = group.Count.ToString("N0"),
                Count = group.Count,
                Percent = Percent(group.Count, max),
                Alpha = 0.16 + intensity * 0.84,
                ShareText = $"{group.Count:N0} тайтл.",
                Accent = $"#{alpha:X2}2D7DD2",
                TextColor = intensity >= 0.48 ? "#FFFFFFFF" : "#FF1F2937"
            });
        }
    }
}
