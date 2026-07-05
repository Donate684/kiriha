using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
    private void ScoreMenu_Opened(object? sender, EventArgs e)
    {
        if (sender is MenuFlyout flyout && flyout.Target is Control target && target.DataContext is AnimeItem item)
        {
            if (DataContext is AnimeListViewModel vm)
            {
                vm.OpenScoreMenuCommand.Execute(item);
            }
        }
    }

    private void ReleaseMapButton_Click(object? sender, RoutedEventArgs e) => ToggleReleaseMap();

    private void ReleaseCloseButton_Click(object? sender, RoutedEventArgs e) => HideReleaseMap();

    private void ReleaseMapOverlay_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F12 && e.Key != Key.Escape)
            return;

        HideReleaseMap();
        e.Handled = true;
    }

    private void ToggleReleaseMap()
    {
        if (ReleaseMapOverlay.IsVisible)
        {
            HideReleaseMap();
            return;
        }

        ShowReleaseMap();
    }

    private void ShowReleaseMap()
    {
        BuildReleaseMap();
        AnimeListScrollViewer.IsVisible = false;
        StatusTabsPanel.IsVisible = false;
        ReleaseMapOverlay.IsVisible = true;
        ReleaseMapButton.Classes.Add("active");
        ReleaseMapOverlay.Focus();
    }

    private void HideReleaseMap()
    {
        ReleaseMapOverlay.IsVisible = false;
        ReleaseMapButton.Classes.Remove("active");
        StatusTabsPanel.IsVisible = true;
        AnimeListScrollViewer.IsVisible = true;
        Focus();
    }

    private void BuildReleaseMap()
    {
        ReleaseTimelinePanel.Children.Clear();
        var palette = CreateReleasePalette();
        ApplyReleaseTheme(palette);

        var releases = GetUpcomingReleases().Take(24).ToList();
        ReleaseEmptyState.IsVisible = releases.Count == 0;

        if (releases.Count == 0)
        {
            ReleaseHeroAnimeTitle.Text = string.Empty;
            ReleaseHeroRussianTitle.Text = string.Empty;
            ReleaseHeroRussianTitle.IsVisible = false;
            ReleaseHeroKindText.Text = "Нет дат";
            ReleaseHeroCountdownText.Text = "после синхронизации";
            ReleaseHeroTimeText.Text = "--:--";
            ReleaseHeroWeekText.Text = "Нет будущих дат в текущих данных";
            CachedImage.SetSource(ReleaseHeroPoster, null);
            return;
        }

        var first = releases[0];
        ReleaseHeroAnimeTitle.Text = first.Title;
        ReleaseHeroRussianTitle.DataContext = first.Item;
        ReleaseHeroRussianTitle[!TextBlock.TextProperty] = new Avalonia.Data.Binding("RussianTitle");
        ReleaseHeroRussianTitle[!TextBlock.IsVisibleProperty] = new Avalonia.Data.Binding("RussianTitle") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty };
        ReleaseHeroKindText.Text = GetHeroReleaseKind(first);
        ReleaseHeroCountdownText.Text = FormatUntilRelease(first.ReleaseAt);
        ReleaseHeroTimeText.Text = first.ReleaseAt.ToString("HH:mm");
        ReleaseHeroWeekText.Text = FormatWeekReleaseSummary(releases);
        CachedImage.SetSource(ReleaseHeroPoster, first.PosterUrl);

        // Group releases by day and build timeline with headers
        DateTime? lastDate = null;
        int staggerIndex = 0;
        const int staggerMs = 40;

        foreach (var release in releases)
        {
            var releaseDate = release.ReleaseAt.Date;

            // Day group header
            if (lastDate == null || releaseDate != lastDate.Value)
            {
                var header = CreateDayHeader(releaseDate, palette);
                // Headers appear immediately
                ReleaseTimelinePanel.Children.Add(header);
                lastDate = releaseDate;
            }

            // Release card with cascade reveal
            var card = CreateReleaseCard(release, palette);
            card.Opacity = 0;
            card.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translate(0,10px)");
            card.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition
                {
                    Property = Avalonia.Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(350),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                },
                new Avalonia.Animation.TransformOperationsTransition
                {
                    Property = Avalonia.Visual.RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(350),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                }
            };
            ReleaseTimelinePanel.Children.Add(card);

            // Schedule reveal with stagger
            var capturedCard = card;
            var delay = TimeSpan.FromMilliseconds(staggerIndex * staggerMs);
            DispatcherTimer.RunOnce(() =>
            {
                capturedCard.Opacity = 1;
                capturedCard.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse("translate(0,0)");
            }, delay);
            staggerIndex++;
        }

        // Same compensation as in settings: the decorated window viewport can be
        // taller than the visible area, so the last card needs scrollable air.
        ReleaseTimelinePanel.Children.Add(new Border
        {
            Height = 110,
            IsHitTestVisible = false,
            Background = Brushes.Transparent
        });

        if (App.Services.GetService(typeof(Kiriha.Services.Data.ShikiMetadataService)) is Kiriha.Services.Data.ShikiMetadataService shiki)
        {
            var tasks = releases.Select(r => shiki.EnsureLocalizedAsync(r.Item));
            _ = Task.WhenAll(tasks);
        }
    }

    private IEnumerable<ReleaseMapItem> GetUpcomingReleases()
    {
        if (DataContext is not AnimeListViewModel vm)
            return Enumerable.Empty<ReleaseMapItem>();

        var now = DateTime.Now;
        return vm.AnimeItems
            .Select(item => CreateReleaseMapItem(item, now))
            .Where(item => item != null)
            .Select(item => item!)
            .OrderBy(item => item.ReleaseAt);
    }

    private static ReleaseMapItem? CreateReleaseMapItem(AnimeItem item, DateTime now)
    {
        if (item.Status == UserAnimeStatus.Dropped)
            return null;

        if (item.NextEpisodeAt.HasValue && item.NextEpisodeAt.Value >= now.AddMinutes(-10))
        {
            var nextEpisode = item.EpisodesAired + 1;
            if (item.TotalEpisodes > 0)
                nextEpisode = Math.Min(nextEpisode, item.TotalEpisodes);
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.NextEpisodeAt.Value,
                nextEpisode > 0 ? $"{nextEpisode} серия" : "следующая серия",
                item.AiringBadgeText,
                item.MainPictureUrl);
        }

        if (item.AiringDate.HasValue && item.AiringDate.Value >= now.Date)
        {
            return new ReleaseMapItem(
                GetPrimaryReleaseTitle(item),
                item,
                item.AiringDate.Value,
                "премьера",
                item.Season,
                item.MainPictureUrl);
        }

        return null;
    }

    private static string GetPrimaryReleaseTitle(AnimeItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.EnglishTitle))
            return item.EnglishTitle;

        return !string.IsNullOrWhiteSpace(item.Title) ? item.Title : item.DisplayTitle;
    }
}
