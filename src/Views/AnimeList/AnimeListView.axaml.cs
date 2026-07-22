using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kiriha.Models;
using Kiriha.ViewModels.AnimeList;

namespace Kiriha.Views.AnimeList;

public partial class AnimeListView : UserControl
{
    private ItemsRepeater? _gridRepeater;

    // Reveal-on-load runs only for the initially rendered page. Recycled
    // cards that enter during scrolling skip reveal transitions to avoid
    // dozens of parallel Opacity/Transform animations hurting scroll FPS.
    private DateTime _lastRevealEvent = DateTime.MinValue;
    private int _revealStaggerIndex;
    // Start as active because ItemsRepeater may realize initial elements
    // before OnLoaded; BeginInitialRevealWindow restarts the timer there.
    private bool _initialRevealActive = true;
    private const int RevealStaggerStepMs = 45;
    private const int RevealStaggerIdleResetMs = 140;
    private static readonly TimeSpan InitialRevealWindow = TimeSpan.FromMilliseconds(1100);

    // Style switching removed. Only Floating Magazine is used.

    public AnimeListView()
    {
        InitializeComponent();
        Focusable = true;
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);

        _gridRepeater = this.FindControl<ItemsRepeater>("AnimeGridRepeater");
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared += OnGridElementPrepared;
            _gridRepeater.ElementClearing += OnGridElementClearing;
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            ToggleReleaseMap();
            e.Handled = true;
            return;
        }


    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (DataContext is not AnimeListViewModel vm) return;
        var settings = vm.SettingsService;

        // Ensure ItemsRepeater uses the Poster First template
        if (_gridRepeater != null && this.TryFindResource("CardTemplatePosterFirst", this.ActualThemeVariant, out var resource) && resource is IDataTemplate dt)
        {
            _gridRepeater.ItemTemplate = dt;
            if (_gridRepeater.Layout is Avalonia.Layout.UniformGridLayout layout)
            {
                layout.MinItemWidth = 172;
                layout.MinItemHeight = 326;
            }
        }

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
        DispatcherTimer.RunOnce(() => _initialRevealActive = false, InitialRevealWindow);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_gridRepeater != null)
        {
            _gridRepeater.ElementPrepared -= OnGridElementPrepared;
            _gridRepeater.ElementClearing -= OnGridElementClearing;
        }

        if (DataContext is AnimeListViewModel vm && vm.FilteredItems != null)
        {
            // Unloading logic handled by AsyncImageLoader automatically or no longer needed
        }

        base.OnUnloaded(e);
    }

    /// <summary>
    /// When an item enters the viewport, enqueue it for image download if needed.
    /// This is the core of the lazy loading mechanism.
    /// </summary>
    private void OnGridElementPrepared(object? sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element.DataContext is AnimeItem item && DataContext is AnimeListViewModel vm)
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
        DispatcherTimer.RunOnce(() => card.Classes.Add("shown"), delay);
    }

    /// <summary>
    /// When an item is recycled (scrolls out of view), the ItemsRepeater 
    /// automatically recycles the UI element. We don't null LocalPosterPath:
    /// the path string is tiny, and keeping it allows instant image reload
    /// when scrolling back. Memory is bounded by the ~30 recycled controls.
    /// </summary>
    private void OnGridElementClearing(object? sender, ItemsRepeaterElementClearingEventArgs e)
    {
        // Reset reveal state on recycled cards so the next preparation can
        // decide whether to animate them.
        if (e.Element is Border card && card.Classes.Contains("revealItem"))
            card.Classes.Remove("shown");
    }

    private void QueueVisibleItems()
    {
        if (_gridRepeater?.ItemsSourceView == null) return;

        // Iterate through currently realized elements to ensure they are queued
        // This handles the case where items were prepared before OnLoaded or events were attached.
        for (int i = 0; i < _gridRepeater.ItemsSourceView.Count; i++)
        {
            var element = _gridRepeater.TryGetElement(i);
            if (element != null && element.DataContext is AnimeItem item)
            {
                if (DataContext is AnimeListViewModel vm)
                {
                    vm.EnqueueItemForViewport(item);
                }
            }
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
    }

    private async void Poster_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        // async void event handler: any leaked exception kills the process. Wrap defensively.
        try
        {
            if (sender is Control c && c.DataContext is Models.AnimeItem item)
            {
                if (DataContext is AnimeListViewModel vm)
                {
                    if (await vm.DialogService.ShowAnimeDetailsAsync(this, item))
                    {
                        // The details dialog might have mutated the item's Status or Rewatching state.
                        // Refresh the view model to apply filters and counts.
                        vm.RefreshAfterDetailsEdit();
                    }
                }
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "AnimeListView.Poster_DoubleTapped failed");
        }
    }






}
