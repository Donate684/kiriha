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
    private void AddRecentHistory(IEnumerable<HistoryItem> history, IReadOnlyCollection<AnimeItem> items)
    {
        var today = DateTime.Now.Date;
        var posterMap = items
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().MainPictureUrl);
        var watched = history
            .Where(x => x.ActionType is 1 or 4 or 6)
            .Where(x => x.Timestamp.Date < today && x.Timestamp.Date >= today.AddDays(-RecentHistoryDays))
            .ToList();

        var grouped = watched
            .GroupBy(x => (today - x.Timestamp.Date).Days)
            .ToDictionary(x => x.Key, x => x.ToList());
        var max = Math.Max(1, grouped.Values.Select(x => x.Count).DefaultIfEmpty().Max());

        RecentHistoryEpisodes = watched.Count;
        RecentHistoryTitles = watched.Select(x => x.AnimeId).Distinct().Count();

        for (var daysAgo = RecentHistoryDays; daysAgo >= 1; daysAgo--)
        {
            grouped.TryGetValue(daysAgo, out var entries);
            var count = entries?.Count ?? 0;
            var date = today.AddDays(-daysAgo);
            var percent = count / (double)max;
            var point = new AnalyticsDailyHistoryPoint
            {
                DaysAgo = daysAgo,
                Label = daysAgo.ToString(CultureInfo.InvariantCulture),
                DateLabel = date.ToString("dd.MM", CultureInfo.CurrentCulture),
                Count = count,
                BarHeight = 3 + percent * 104,
                Alpha = count == 0 ? 0.16 : 0.35 + percent * 0.65,
                CountLabel = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty,
                ShowCountInBar = percent >= 0.32,
                Tooltip = $"{date:dd.MM}: {count} эп."
            };

            foreach (var entry in entries?.OrderByDescending(x => x.Timestamp) ?? Enumerable.Empty<HistoryItem>())
            {
                posterMap.TryGetValue(entry.AnimeId, out var posterUrl);
                point.Entries.Add(new AnalyticsHistoryEntry
                {
                    Title = entry.RussianTitle ?? entry.AnimeTitle,
                    Subtitle = entry.RussianTitle != null ? entry.AnimeTitle : null,
                    Detail = entry.Episode > 0
                        ? $"Серия {entry.Episode} · {entry.Timestamp:HH:mm}"
                        : entry.Timestamp.ToString("HH:mm", CultureInfo.CurrentCulture),
                    PosterUrl = posterUrl
                });
            }

            RecentHistory.Add(point);
        }
    }

    private void AddMonthlyHistory(IEnumerable<AnimeItem> completed)
    {
        var monthGroups = completed
            .Where(x => x.DateCompleted.HasValue && x.DateCompleted.Value.Year > 1900)
            .GroupBy(x => new { x.DateCompleted!.Value.Year, x.DateCompleted.Value.Month })
            .ToDictionary(x => (x.Key.Year, x.Key.Month), x => x.ToList());

        HasMonthlyHistory = monthGroups.Count > 0;
        if (!HasMonthlyHistory) return;

        var max = Math.Max(1, monthGroups.Values.Max(x => x.Count));
        var now = DateTime.Now;
        var minYear = Math.Min(monthGroups.Keys.Min(x => x.Year), now.Year);
        var maxYear = Math.Max(monthGroups.Keys.Max(x => x.Year), now.Year);
        var monthNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames;

        for (var year = maxYear; year >= minYear; year--)
        {
            var row = new AnalyticsMonthlyHistoryRow { Year = year };
            for (var month = 1; month <= 12; month++)
            {
                monthGroups.TryGetValue((year, month), out var entries);
                var count = entries?.Count ?? 0;
                var mean = entries?
                    .Select(x => int.TryParse(x.Score, out var score) ? score : 0)
                    .Where(x => x > 0)
                    .DefaultIfEmpty()
                    .Average() ?? 0;
                var intensity = count == 0 ? 0 : count / (double)max;
                var alpha = count == 0
                    ? (byte)0x10
                    : (byte)Math.Round(0x32 + intensity * (0xFF - 0x32));

                var cell = new AnalyticsMonthlyHistoryCell
                {
                    Month = month,
                    MonthName = monthNames[month - 1],
                    Count = count,
                    Alpha = count == 0 ? 0.06 : 0.22 + count / (double)max * 0.78,
                    Fill = $"#{alpha:X2}2D7DD2",
                    TextColor = intensity >= 0.48 ? "#FFFFFFFF" : "#FF1F2937",
                    IsCurrentMonth = year == now.Year && month == now.Month,
                    Tooltip = mean > 0
                        ? $"{monthNames[month - 1]} {year}: {count} завершено, средняя {mean:0.00}"
                        : $"{monthNames[month - 1]} {year}: {count} завершено"
                };

                foreach (var entry in entries?.OrderBy(x => x.DisplayTitle) ?? Enumerable.Empty<AnimeItem>())
                {
                    cell.Entries.Add(new AnalyticsHistoryEntry
                    {
                        Title = entry.DisplayTitle,
                        Subtitle = entry.RussianTitle != null ? entry.Title : null,
                        Detail = int.TryParse(entry.Score, out var score) && score > 0
                            ? $"Оценка {score}"
                            : "Без оценки",
                        PosterUrl = entry.MainPictureUrl
                    });
                }

                row.Months.Add(cell);
            }


            MonthlyHistory.Add(row);
        }
    }

    private static void AddFavoriteRows(
        ObservableCollection<AnalyticsFavoriteRow> target,
        IEnumerable<AnimeItem> items,
        Func<AnimeItem, IEnumerable<string>> selector,
        Func<string, string>? nameFormatter = null)
    {
        var groups = items
            .SelectMany(item => selector(item)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new { Key = value.Trim(), Item = item }))
            .GroupBy(x => x.Key)
            .Select(group =>
            {
                var entries = group.Select(x => x.Item).DistinctBy(x => x.Id).ToList();
                var scores = entries
                    .Select(x => int.TryParse(x.Score, out var score) ? score : 0)
                    .Where(x => x > 0)
                    .ToList();
                var mean = scores.Count > 0 ? scores.Average() : 0;
                var weighted = scores.Count > 0 ? FavoriteScore(mean, entries.Count) : 0;
                var hours = EstimateHoursWatched(entries);

                return new
                {
                    Name = group.Key,
                    Count = entries.Count,
                    Mean = mean,
                    Weighted = weighted,
                    Hours = hours,
                    Entries = entries
                };
            })
            .OrderByDescending(x => x.Weighted)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.Name)
            .Take(30)
            .ToList();

        var totalCompleted = items.Count(x => x.Status == UserAnimeStatus.Completed);
        if (totalCompleted <= 0) totalCompleted = 1;

        var rank = 1;
        foreach (var group in groups)
        {
            var name = nameFormatter?.Invoke(group.Name) ?? group.Name;
            var mean = group.Mean > 0 ? group.Mean.ToString("0.00") : "-";
            var weighted = group.Weighted > 0 ? group.Weighted.ToString("0.00") : "-";
            var hours = $"{group.Hours:0.0} ч";

            var completedInGroup = group.Entries.Count(x => x.Status == UserAnimeStatus.Completed);
            var percentCompleted = totalCompleted > 0 ? (completedInGroup * 100.0 / totalCompleted) : 0;

            var row = new AnalyticsFavoriteRow
            {
                Rank = rank++,
                Name = name,
                Count = group.Count,
                MeanScore = mean,
                WeightedScore = weighted,
                TimeSpent = hours,
                Summary = $"{group.Count} тайтл. • оценка {mean} • {hours}",
                Percent = percentCompleted,
                Accent = GetAccent(group.Name)
            };

            foreach (var entry in group.Entries
                         .OrderByDescending(x => int.TryParse(x.Score, out var score) ? score : 0)
                         .ThenBy(x => x.DisplayTitle)
                         .Select(x => new AnalyticsHistoryEntry
                         {
                             Title = x.DisplayTitle,
                             Subtitle = x.RussianTitle != null ? x.Title : null,
                             Detail = int.TryParse(x.Score, out var score) && score > 0
                                 ? $"Оценка {score}"
                                 : DisplayTotal(x),
                             PosterUrl = x.MainPictureUrl
                         }))
            {
                row.Entries.Add(entry);
}

            target.Add(row);
        }
    }

    private static double FavoriteScore(double meanScore, int count)
    {
        const double globalMean = 5.5;
        const double smoothing = 10.0;

        // 1. Байесовское среднее (отсекает жанры с 1-2 тайтлами на 10/10)
        var bayesianMean = (meanScore * count + globalMean * smoothing) / (count + smoothing);

        // 2. Логарифмический бонус за количество просмотренного
        // Log10(10) = 1, Log10(100) = 2. Плавно награждает за объём.
        var volumeBonus = Math.Log10(Math.Max(1, count));

        return bayesianMean + volumeBonus;
    }

    [RelayCommand]
    private void OpenFavorite(AnalyticsFavoriteRow? row)
    {
        if (row == null || row.Entries.Count == 0)
        {
            return;
        }

        HistoryPopupTitle = row.Name;
        HistoryPopupSubtitle = $"{row.Count} тайтл. • средняя {row.MeanScore} • вес {row.WeightedScore}";
        ShowHistoryPopup(row.Entries);
    }

    [RelayCommand]
    private void OpenDailyHistory(AnalyticsDailyHistoryPoint? point)
    {
        if (point == null || point.Count == 0) return;

        HistoryPopupTitle = point.DaysAgo == 1
            ? "Вчера"
            : $"{point.DaysAgo} дн. назад";
        HistoryPopupSubtitle = $"{point.DateLabel} · {point.Count} эп. · {point.Entries.Select(x => x.Title).Distinct().Count()} тайтл(ов)";
        ShowHistoryPopup(point.Entries);
    }

    [RelayCommand]
    private void OpenMonthlyHistory(AnalyticsMonthlyHistoryCell? cell)
    {
        if (cell == null || cell.Count == 0) return;

        HistoryPopupTitle = $"{cell.MonthName} · завершено";
        HistoryPopupSubtitle = $"{cell.Count} тайтл(ов)";
        ShowHistoryPopup(cell.Entries);
    }

    [RelayCommand]
    private void CloseHistoryPopup()
    {
        IsHistoryPopupOpen = false;
        HistoryPopupEntries.Clear();
    }

    private void ShowHistoryPopup(IEnumerable<AnalyticsHistoryEntry> entries)
    {
        HistoryPopupEntries.Clear();
        foreach (var entry in entries)
        {
            HistoryPopupEntries.Add(entry);
        }

        IsHistoryPopupOpen = true;
    }

    private static double Percent(int value, int max) => Math.Clamp(value * 100.0 / Math.Max(1, max), 0, 100);

    private static double PercentDouble(double value, double max) => Math.Clamp(value * 100.0 / Math.Max(0.01, max), 0, 100);

    private static string GetAccent(string label)
    {
        var hash = Math.Abs(label.GetHashCode());
        var palette = new[]
        {
            "#FF0F7B83",
            "#FF2D7DD2",
            "#FFD17A22",
            "#FF7B61FF",
            "#FF2E9D62",
            "#FFD1495B",
            "#FF5C80BC",
            "#FF8E6C88"
        };

        return palette[hash % palette.Length];
    }

    private static string GetStatusLabel(UserAnimeStatus status)
    {
        return status switch
        {
            UserAnimeStatus.Watching => UIUtils.GetLoc("anime.status.watching"),
            UserAnimeStatus.Completed => UIUtils.GetLoc("anime.status.completed"),
            UserAnimeStatus.OnHold => UIUtils.GetLoc("anime.status.on_hold"),
            UserAnimeStatus.Dropped => UIUtils.GetLoc("anime.status.dropped"),
            UserAnimeStatus.PlanToWatch => UIUtils.GetLoc("anime.status.plan_to_watch"),
            _ => UIUtils.GetLoc("anime.status.unknown")
        };
    }

    private static string GetStatusAccent(UserAnimeStatus status)
    {
        return status switch
        {
            UserAnimeStatus.Watching => "#FF2D7DD2",
            UserAnimeStatus.Completed => "#FF2E9D62",
            UserAnimeStatus.OnHold => "#FFD17A22",
            UserAnimeStatus.Dropped => "#FFE53935",
            UserAnimeStatus.PlanToWatch => "#FF7B61FF",
            _ => "#FF6B7280"
        };
    }
}
