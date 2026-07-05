using System;
using Kiriha.Models;
using Kiriha.Models.Entities;
namespace Kiriha.Views;

public partial class AnimeListView
{
    private sealed record ReleaseMapItem(string Title, AnimeItem Item, DateTime ReleaseAt, string Kind, string Note, string? PosterUrl);

    private sealed record ReleasePalette(
        string Surface,
        string HeaderSurface,
        string HeaderScrimStart,
        string HeaderScrimMid,
        string HeaderScrimEnd,
        string CardBackground,
        string CardBorder,
        string CardHover,
        string PosterBackground,
        string PrimaryText,
        string SecondaryTitleText,
        string SecondaryText,
        string CoolAccent,
        string WarmAccent,
        string CoolBadge,
        string WarmBadge,
        string CoolBadgeText,
        string WarmBadgeText,
        string PillBg,
        string DayHeaderText,
        string Divider);
}
