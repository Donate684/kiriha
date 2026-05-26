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
    private void AddProfileTodos(IReadOnlyCollection<AnimeItem> items)
    {
        var now = DateTime.Now;
        foreach (var item in items
                     .Where(x => IsCurrentOngoing(x, now) && x.ShowAiredProgressBar && x.UnseenEpisodesCount > 0)
                     .OrderByDescending(x => x.UnseenEpisodesCount)
                     .ThenByDescending(x => x.NextEpisodeAt ?? DateTime.MinValue)
                     .Take(6))
        {
            WatchTodo.Add(ToTodo(
                item,
                $"{item.UnseenEpisodesCount} эп.",
                $"Просмотрено {item.Progress}/{DisplayTotal(item)}",
                "#FFE53935"));
        }

        foreach (var item in items
                     .Where(x => !IsCurrentOngoing(x, now)
                                 && x.Status == UserAnimeStatus.Watching
                                 && x.ShowAiredProgressBar
                                 && x.UnseenEpisodesCount > 0)
                     .OrderByDescending(x => x.UnseenEpisodesCount)
                     .ThenByDescending(ParsedMeanScore)
                     .Take(6))
        {
            FinishedWatchTodo.Add(ToTodo(
                item,
                $"{item.UnseenEpisodesCount} эп.",
                $"Просмотрено {item.Progress}/{DisplayTotal(item)}",
                "#FF7B61FF"));
        }

        foreach (var item in items
                     .Where(x => x.NextEpisodeAt.HasValue && x.NextEpisodeAt.Value >= now)
                     .OrderBy(x => x.NextEpisodeAt)
                     .Take(6))
        {
            UpcomingTodo.Add(ToTodo(
                item,
                item.AiringBadgeText,
                FormatUpcomingDetail(item),
                "#FF2D7DD2"));
        }

        foreach (var item in items
                     .Where(x => x.Status == UserAnimeStatus.PlanToWatch)
                     .OrderByDescending(ParsedMeanScore)
                     .ThenBy(x => x.Popularity == 0 ? int.MaxValue : x.Popularity)
                     .Take(6))
        {
            PlanTodo.Add(ToTodo(
                item,
                item.MeanScore ?? "-",
                FormatPlanDetail(item),
                "#FF2E9D62"));
        }

        foreach (var item in items
                     .Where(x => x.Status == UserAnimeStatus.Watching && x.Progress > 0 && !IsCurrentOngoing(x, now))
                     .OrderBy(x => x.DateStarted ?? DateTime.MaxValue)
                     .ThenBy(x => x.LastEpisodeAt ?? DateTime.MaxValue)
                     .Take(6))
        {
            var pauseFrom = item.DateStarted ?? item.LastEpisodeAt ?? now;
            var days = Math.Max(1, (int)(now - pauseFrom).TotalDays);
            StaleTodo.Add(ToTodo(
                item,
                $"{days} дн.",
                $"Пауза на {item.Progress}/{DisplayTotal(item)}",
                "#FFD17A22"));
        }
    }

    private static ProfileTodoItem ToTodo(AnimeItem item, string badge, string detail, string accent)
    {
        return new ProfileTodoItem
        {
            Title = item.DisplayTitle,
            Subtitle = item.RussianTitle != null ? item.Title : null,
            Badge = badge,
            Detail = detail,
            PosterUrl = item.MainPictureUrl,
            Accent = accent
        };
    }

    private static string FormatUpcomingDetail(AnimeItem item)
    {
        var date = item.NextEpisodeAt?.ToString("dd.MM HH:mm", CultureInfo.CurrentCulture) ?? "-";
        if (string.Equals(item.Type, Constants.AnimeTypes.Movie, StringComparison.OrdinalIgnoreCase))
        {
            return $"Фильм • {date}";
        }

        var nextEpisode = item.EpisodesAired > 0 ? item.EpisodesAired + 1 : 1;
        return $"{nextEpisode} серия • {date}";
    }

    private static string FormatPlanDetail(AnimeItem item)
    {
        if (item.Genres.Count > 0)
        {
            return string.Join(", ", item.Genres.Take(2).Select(LocalizeGenre));
        }

        var type = UIUtils.GetLoc($"anime.types.{item.Type}");
        return type == $"anime.types.{item.Type}" ? item.Type : type;
    }

    private static string LocalizeGenre(string genre)
    {
        var candidates = new[]
        {
            genre.ToLowerInvariant().Replace(" ", string.Empty),
            ToResourceKey(genre),
            genre.ToLowerInvariant()
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = $"genres.{candidate}";
            var translated = UIUtils.GetLoc(key);
            if (translated != key)
            {
                return translated;
            }
        }

        return genre;
    }

    private static string ToResourceKey(string value)
    {
        var chars = new List<char>(value.Length);
        var lastWasSeparator = false;
        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Add(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                chars.Add('_');
                lastWasSeparator = true;
            }
        }

        return new string(chars.ToArray()).Trim('_');
    }

    private static bool IsCurrentOngoing(AnimeItem item, DateTime now)
    {
        if (!string.Equals(item.StatusDetailed, "currently_airing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (item.NextEpisodeAt.HasValue)
        {
            return item.NextEpisodeAt.Value >= now.AddDays(-14);
        }

        if (item.LastEpisodeAt.HasValue)
        {
            return item.LastEpisodeAt.Value >= now.AddDays(-21);
        }

        if (item.StartYear == now.Year && string.Equals(item.StartSeason, GetSeason(now.Month), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string GetSeason(int month)
    {
        return month switch
        {
            12 or 1 or 2 => Constants.Seasons.Winter,
            3 or 4 or 5 => Constants.Seasons.Spring,
            6 or 7 or 8 => Constants.Seasons.Summer,
            _ => Constants.Seasons.Fall
        };
    }

    private static string DisplayTotal(AnimeItem item) => item.TotalEpisodes > 0 ? item.TotalEpisodes.ToString(CultureInfo.InvariantCulture) : "?";

    private static double ParsedMeanScore(AnimeItem item) =>
        double.TryParse(item.MeanScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
            ? score
            : 0;
}
