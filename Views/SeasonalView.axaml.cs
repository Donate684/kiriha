using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Views;

public partial class SeasonalView : UserControl
{
    private ItemsRepeater? _gridRepeater;
    // Reveal-on-load runs only for cards realized during the initial page
    // render, or after the displayed dataset changes. Recycled cards entering
    // during scrolling skip reveal transitions to avoid scroll FPS drops.
    private DateTime _lastRevealEvent = DateTime.MinValue;
    private int _revealStaggerIndex;
    // Start as active because ItemsRepeater may realize initial elements
    // before OnLoaded; BeginInitialRevealWindow restarts the timer there.
    private bool _initialRevealActive = true;
    private const int RevealStaggerStepMs = 45;
    private const int RevealStaggerIdleResetMs = 140;
    // Reveal duration: about 12 cards * 45 ms plus the 450 ms transition.
    private static readonly TimeSpan InitialRevealWindow = TimeSpan.FromMilliseconds(1100);

    public SeasonalView()
    {
        InitializeComponent();
        _gridRepeater = this.FindControl<ItemsRepeater>("SeasonalItemsRepeater");
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared += OnGridElementPrepared;
            _gridRepeater.ElementClearing += OnGridElementClearing;
        }
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BeginInitialRevealWindow();
        // Initial viewport kickstart
        Avalonia.Threading.Dispatcher.UIThread.Post(QueueVisibleItems, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Opens a short window where prepared cards play the reveal cascade.
    /// After it closes, cards entering the viewport during scroll show instantly.
    /// </summary>
    private void BeginInitialRevealWindow()
    {
        _initialRevealActive = true;
        _revealStaggerIndex = 0;
        Avalonia.Threading.DispatcherTimer.RunOnce(() => _initialRevealActive = false, InitialRevealWindow);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared -= OnGridElementPrepared;
            _gridRepeater.ElementClearing -= OnGridElementClearing;
        }
        if (DataContext is SeasonalViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        base.OnUnloaded(e);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is SeasonalViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeasonalViewModel.DisplayItems))
        {
            // Dataset changes from filtering/sorting count as a fresh render,
            // so reopen the reveal cascade window.
            BeginInitialRevealWindow();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ContentScrollViewer.Offset = Avalonia.Vector.Zero;
                QueueVisibleItems();
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnGridElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element.DataContext is Models.AnimeItem item && DataContext is SeasonalViewModel vm)
        {
            vm.EnqueueItemForViewport(item);
        }

        if (e.Element is not Border card || !card.Classes.Contains("revealItem"))
            return;

        // Outside the initial reveal window this is a scroll-entering card.
        // Remove reveal classes so it appears immediately without transitions.
        if (!_initialRevealActive)
        {
            card.Classes.Remove("revealItem");
            card.Classes.Remove("shown");
            return;
        }

        // Reveal-on-load: staggered appearance delay.
        card.Classes.Remove("shown");

        var now = DateTime.UtcNow;
        if ((now - _lastRevealEvent).TotalMilliseconds > RevealStaggerIdleResetMs)
            _revealStaggerIndex = 0;
        _lastRevealEvent = now;

        var delay = TimeSpan.FromMilliseconds(_revealStaggerIndex++ * RevealStaggerStepMs);
        Avalonia.Threading.DispatcherTimer.RunOnce(() => card.Classes.Add("shown"), delay);
    }

    private void OnGridElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        // Reset reveal state on recycled cards.
        if (e.Element is Border card && card.Classes.Contains("revealItem"))
            card.Classes.Remove("shown");
    }

    private void QueueVisibleItems()
    {
        if (_gridRepeater?.ItemsSourceView == null || _gridRepeater.ItemsSourceView.Count == 0) return;

        bool foundAny = false;
        for (int i = 0; i < Math.Min(_gridRepeater.ItemsSourceView.Count, 50); i++) // Check first 50 items
        {
            var element = _gridRepeater.TryGetElement(i);
            if (element != null && element.DataContext is AnimeItem item)
            {
                if (DataContext is SeasonalViewModel vm)
                {
                    vm.EnqueueItemForViewport(item);
                    foundAny = true;
                }
            }
        }

        // If we didn't find any elements, they might still be layouting. Try once more in a bit.
        if (!foundAny && _gridRepeater.ItemsSourceView.Count > 0)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                QueueVisibleItems();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private async void Poster_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        // async void event handler: any leaked exception kills the process. Wrap defensively.
        try
        {
            if (sender is Control c && c.DataContext is Models.AnimeItem item)
            {
                await App.Services.GetRequiredService<Kiriha.Core.Dialogs.IDialogService>().ShowAnimeDetailsAsync(this, item);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalView.Poster_DoubleTapped failed");
        }
    }

    // Two-step confirm for the seasonal hide button. We track the currently
    // expanded item so a tap on a different card collapses the previous one,
    // and we auto-collapse on a short timeout if the user walks away.
    private Models.AnimeItem? _hideConfirmItem;
    private Avalonia.Threading.DispatcherTimer? _hideConfirmTimer;
    private static readonly TimeSpan HideConfirmTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Single tap on the eye button: first tap expands the pill into "Hide?"
    /// (or "Restore?" when already hidden); a second tap on the same card
    /// commits the toggle. Marks the event handled so the underlying poster
    /// double-tap (which opens details) won't fire.
    /// </summary>
    private void HideBtn_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        try
        {
            if (sender is not Control c || c.DataContext is not Models.AnimeItem item) return;
            if (DataContext is not SeasonalViewModel vm) return;
            e.Handled = true;

            if (item.IsHideConfirming)
            {
                // Second tap = commit.
                ResetHideConfirm();
                vm.ToggleHiddenSeasonalCommand.Execute(item);
                return;
            }

            // First tap: collapse any previous prompt, then expand this one.
            ResetHideConfirm();
            item.IsHideConfirming = true;
            _hideConfirmItem = item;
            _hideConfirmTimer = new Avalonia.Threading.DispatcherTimer { Interval = HideConfirmTimeout };
            _hideConfirmTimer.Tick += (_, _) => ResetHideConfirm();
            _hideConfirmTimer.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SeasonalView.HideBtn_Tapped failed");
        }
    }

    private void ResetHideConfirm()
    {
        if (_hideConfirmTimer != null)
        {
            _hideConfirmTimer.Stop();
            _hideConfirmTimer = null;
        }
        if (_hideConfirmItem != null)
        {
            _hideConfirmItem.IsHideConfirming = false;
            _hideConfirmItem = null;
        }
    }
}
