using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.ViewModels;

namespace Kiriha.Views;

public partial class AnimeListView
{
    private ReleasePalette CreateReleasePalette()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        return dark
            ? new ReleasePalette(
                Surface: "#FF0D1117",
                HeaderSurface: "#FF1A1F2E",
                HeaderScrimStart: "#F21A1F2E",
                HeaderScrimMid: "#D01A1F2E",
                HeaderScrimEnd: "#101A1F2E",
                CardBackground: "#161B27",
                CardBorder: "#22FFFFFF",
                CardHover: "#1E2435",
                PosterBackground: "#0F1420",
                PrimaryText: "#F0FFFFFF",
                SecondaryTitleText: "#FF8BD3FF",
                SecondaryText: "#80FFFFFF",
                CoolAccent: "#FF8BD3FF",
                WarmAccent: "#FFE9C46A",
                CoolBadge: "#FF1E3A5F",
                WarmBadge: "#FF3D2A0A",
                CoolBadgeText: "#FF8BD3FF",
                WarmBadgeText: "#FFE9C46A",
                PillBg: "#18FFFFFF",
                DayHeaderText: "#55FFFFFF",
                Divider: "#20FFFFFF")
            : new ReleasePalette(
                Surface: "#EEFFFFFF",
                HeaderSurface: "#FFEAF3FB",
                HeaderScrimStart: "#F8EAF3FB",
                HeaderScrimMid: "#DDEAF3FB",
                HeaderScrimEnd: "#40EAF3FB",
                CardBackground: "#F7FFFFFF",
                CardBorder: "#18000000",
                CardHover: "#FFFFFFFF",
                PosterBackground: "#10000000",
                PrimaryText: "#E4000000",
                SecondaryTitleText: "#FF087CC5",
                SecondaryText: "#99000000",
                CoolAccent: "#FF087CC5",
                WarmAccent: "#FFD39A18",
                CoolBadge: "#FFE6F3FC",
                WarmBadge: "#FFFFF3D0",
                CoolBadgeText: "#FF087CC5",
                WarmBadgeText: "#FF8A6200",
                PillBg: "#0E000000",
                DayHeaderText: "#66000000",
                Divider: "#16000000");
    }

    private void ApplyReleaseTheme(ReleasePalette palette)
    {
        ReleaseMapOverlay.Background = BrushFrom(palette.Surface);
        ReleaseMapOverlay.BorderBrush = BrushFrom(palette.CardBorder);
    }

    private static string FormatRelativeDate(DateTime releaseAt)
    {
        var today = DateTime.Today;
        var date = releaseAt.Date;
        if (date == today) return "сегодня";
        if (date == today.AddDays(1)) return "завтра";
        var diff = (date - today).Days;
        return diff > 1 ? $"через {diff} дн." : releaseAt.ToString("dd MMM", CultureInfo.CurrentCulture);
    }

    private static string FormatBadgeDate(DateTime releaseAt)
    {
        var today = DateTime.Today;
        var date = releaseAt.Date;
        if (date == today)
            return "Сегодня";
        if (date == today.AddDays(1))
            return "Завтра";

        var diff = (date - today).Days;
        if (diff > 0)
            return $"{diff} дн.";

        return releaseAt.ToString("dd MMM", CultureInfo.CurrentCulture);
    }

    private static string FormatMonthShort(DateTime date)
    {
        var culture = GetReleaseCulture();
        return date.ToString("MMM", culture).TrimEnd('.').ToUpper(culture);
    }

    private static CultureInfo GetReleaseCulture()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
            ? CultureInfo.GetCultureInfo("ru-RU")
            : CultureInfo.CurrentCulture;
    }

    private static string GetHeroReleaseKind(ReleaseMapItem release)
    {
        return release.Kind.Contains("премьера", StringComparison.OrdinalIgnoreCase)
            ? "Премьера"
            : release.Kind;
    }

    private static string FormatWeekReleaseSummary(IReadOnlyCollection<ReleaseMapItem> releases)
    {
        var end = DateTime.Now.AddDays(7);
        var count = releases.Count(release => release.ReleaseAt < end);
        return $"{count} {PluralizeRelease(count)} на этой неделе";
    }

    private static string PluralizeRelease(int count)
    {
        var lastTwo = count % 100;
        if (lastTwo is >= 11 and <= 14)
            return "релизов";

        return (count % 10) switch
        {
            1 => "релиз",
            >= 2 and <= 4 => "релиза",
            _ => "релизов"
        };
    }

    private static string FormatUntilRelease(DateTime releaseAt)
    {
        var diff = releaseAt - DateTime.Now;
        if (diff.TotalMinutes <= 1)
            return "сейчас";

        if (diff.TotalDays >= 1)
        {
            var days = (int)Math.Floor(diff.TotalDays);
            var hours = diff.Hours;
            return hours > 0 ? $"{days}д {hours}ч" : $"{days}д";
        }

        if (diff.TotalHours >= 1)
            return $"{(int)Math.Floor(diff.TotalHours)}ч {diff.Minutes}м";

        return $"{Math.Max(1, diff.Minutes)}м";
    }

    private static string ToTransparent(string color)
    {
        if (color.Length == 9 && color[0] == '#')
            return "#00" + color[3..];

        if (color.Length == 7 && color[0] == '#')
            return "#00" + color[1..];

        return "#00000000";
    }

    private static IBrush BrushFrom(string color) => new SolidColorBrush(Color.Parse(color));
}