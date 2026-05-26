using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kiriha.Core.Mpv;
using Kiriha.ViewModels;

namespace Kiriha.Views;

public partial class PlayerOverlayWindow
{
    private void DisableLegacySettingsFlyout()
    {
        if (this.FindControl<Button>("SettingsButton") is { } settingsButton)
            settingsButton.Flyout = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        _hideTimer.Tick -= OnHideTimerTick;
        RemoveHandler(DragDrop.DropEvent, OnDrop);
        RemoveHandler(KeyDownEvent, OnOverlayKeyDown);

        var slider = this.FindControl<Slider>("TimelineSlider");
        if (slider != null)
        {
            slider.RemoveHandler(PointerPressedEvent, OnSliderPointerPressed);
            slider.RemoveHandler(PointerReleasedEvent, OnSliderPointerReleased);
        }

        if (this.FindControl<Button>("ScreenshotButton") is { } screenshotButton)
            screenshotButton.RemoveHandler(PointerReleasedEvent, OnScreenshotButtonPointerReleased);

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.Chapters.CollectionChanged -= OnChaptersChanged;
            if (_viewModelPropertyChanged != null)
                _subscribedViewModel.PropertyChanged -= _viewModelPropertyChanged;
        }

        if (_ownerWindow != null)
        {
            if (_ownerPositionChanged != null)
                _ownerWindow.PositionChanged -= _ownerPositionChanged;
            if (_ownerPropertyChanged != null)
                _ownerWindow.PropertyChanged -= _ownerPropertyChanged;
        }

        _subscribedViewModel = null;
        _viewModelPropertyChanged = null;
        _ownerPositionChanged = null;
        _ownerPropertyChanged = null;

        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.Duration))
            DrawChapterMarkers();
        else if (e.PropertyName == nameof(PlayerViewModel.ShowChapterMarkers))
            DrawChapterMarkers();
        else if (e.PropertyName == nameof(PlayerViewModel.AutoHideControls)
                 && sender is PlayerViewModel { AutoHideControls: false })
            ShowControls();
    }

    private void OnOwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.BoundsProperty || e.Property == Window.ClientSizeProperty)
        {
            UpdateOverlayPosition();
        }
        else if (e.Property == Window.WindowStateProperty)
        {
            WindowState = (WindowState)e.NewValue!;
            UpdateCornerRounding();
        }
    }

    private void UpdateCornerRounding()
    {
        var closeBtn = this.FindControl<Button>("CloseButton");
        var maximizeIcon = this.FindControl<TextBlock>("MaximizeIcon");
        var topBar = this.FindControl<Border>("TopBar");
        var bottomBar = this.FindControl<Border>("BottomBar");
        
        bool isFullscreen = WindowState == WindowState.FullScreen;
        bool isEdgeToEdge = isFullscreen || WindowState == WindowState.Maximized;

        if (maximizeIcon != null)
        {
            maximizeIcon.Text = isFullscreen
                ? "\uE923"
                : "\uE922";
        }
        
        // Remove rounded corners from the actual window by changing decorations and client area hint
        if (_ownerWindow != null)
        {
            _ownerWindow.WindowDecorations = isEdgeToEdge ? WindowDecorations.None : WindowDecorations.BorderOnly;
            _ownerWindow.ExtendClientAreaToDecorationsHint = !isEdgeToEdge;
        }

        // Remove rounded corner from the close button so it stays flush
        if (closeBtn != null)
        {
            closeBtn.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(0, 8, 0, 0);
        }

        // Remove rounded corners from the Top and Bottom shadow gradients
        if (topBar != null)
        {
            topBar.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(8, 8, 0, 0);
        }
        if (bottomBar != null)
        {
            bottomBar.CornerRadius = isEdgeToEdge ? new CornerRadius(0) : new CornerRadius(0, 0, 8, 8);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Chapter markers
    // ──────────────────────────────────────────────────────────

    private void OnChaptersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DrawChapterMarkers();
    }

    private void OnChapterCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        DrawChapterMarkers();
    }

    private void DrawChapterMarkers()
    {
        if (_chapterCanvas == null) return;
        _chapterCanvas.Children.Clear();

        if (DataContext is not PlayerViewModel vm) return;
        if (!vm.ShowChapterMarkers) return;
        if (vm.Duration <= 0 || vm.Chapters.Count == 0) return;

        double trackWidth = _chapterCanvas.Bounds.Width;
        double trackHeight = _chapterCanvas.Bounds.Height;
        if (trackWidth <= 0 || trackHeight <= 0) return;

        // Color matching the timeline thumb/track
        IBrush markerFill = Brushes.White;
        if (Application.Current!.TryGetResource("SystemAccentColor", out var res) && res is Color accentColor)
        {
            markerFill = new SolidColorBrush(accentColor);
        }

        foreach (var chapter in vm.Chapters)
        {
            // Skip the very first chapter (always at t=0)
            if (chapter.Time <= 0) continue;

            double ratio = Math.Clamp(chapter.Time / vm.Duration, 0.0, 1.0);
            double x     = ratio * trackWidth;

            // Two small triangles flanking the 4px timeline track.
            var marker = new Path
            {
                Data              = Geometry.Parse("M0,0 L6,0 L3,3 Z M0,10 L6,10 L3,7 Z"),
                Fill              = markerFill,
                Opacity           = 0.90,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Center
            };

            // Wrap in a border to provide a larger hit-test area for easier clicking
            var hitArea = new Border
            {
                Background = Brushes.Transparent, // Transparent but hit-testable
                Cursor     = new Cursor(StandardCursorType.Hand),
                Width      = 32, // Match the new 32px height of the Slider
                Height     = 32,
                Child      = marker
            };

            double chapterTime = chapter.Time;
            hitArea.PointerPressed += (s, e) =>
            {
                if (DataContext is PlayerViewModel playerVm)
                {
                    playerVm.SeekTo(chapterTime);
                }
            };

            // Track is vertically centered in the Canvas
            double centerY = trackHeight / 2.0;

            // Center the 32x32 hit area on the track
            Canvas.SetLeft(hitArea, x - 16.0);
            Canvas.SetTop(hitArea,  centerY - 16.0);
            _chapterCanvas.Children.Add(hitArea);
        }
    }

    // ──────────────────────────────────────────────────────────
    // Auto-hide logic
    // ──────────────────────────────────────────────────────────
}
