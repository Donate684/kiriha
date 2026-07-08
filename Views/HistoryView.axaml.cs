using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;

namespace Kiriha.Views;

public partial class HistoryView : UserControl
{
    // Reveal-on-load каскад для строк таймлайна.
    private DateTime _lastRevealEvent = DateTime.MinValue;
    private int _revealStaggerIndex;
    private const int RevealStaggerStepMs = 30;
    private const int RevealStaggerIdleResetMs = 140;

    public HistoryView()
    {
        InitializeComponent();
    }

    private void OnHistoryRowLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Border card) return;

        card.Classes.Remove("shown");

        var now = DateTime.UtcNow;
        if ((now - _lastRevealEvent).TotalMilliseconds > RevealStaggerIdleResetMs)
            _revealStaggerIndex = 0;
        _lastRevealEvent = now;

        var delay = TimeSpan.FromMilliseconds(_revealStaggerIndex++ * RevealStaggerStepMs);
        DispatcherTimer.RunOnce(() => card.Classes.Add("shown"), delay);
    }

    private void HistoryItem_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Control c && c.DataContext is HistoryEntryVm entry)
        {
            if (DataContext is HistoryViewModel vm)
            {
                vm.OpenAnimeDetailsCommand.Execute(entry);
            }
        }
    }
}
