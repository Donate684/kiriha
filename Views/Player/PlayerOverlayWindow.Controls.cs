using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
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
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;

namespace Kiriha.Views.Player;

public partial class PlayerOverlayWindow
{
    private void ShowControls()
    {
        var now = DateTime.UtcNow;
        if (!_controlsVisible)
        {
            _controlsVisible = true;
            if (_topBar != null) 
            {
                _topBar.Opacity = 1;
                _topBar.IsHitTestVisible = true;
            }
            if (_bottomBar != null) 
            {
                _bottomBar.Opacity = 1;
                _bottomBar.IsHitTestVisible = true;
            }
            Cursor = s_arrowCursor;
        }

        if (now - _lastControlsKeepAliveUtc < ControlsKeepAliveInterval)
            return;

        _lastControlsKeepAliveUtc = now;
        // Reset the hide timer
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideControls()
    {
        _controlsVisible = false;
        _lastControlsKeepAliveUtc = DateTime.MinValue;
        if (_topBar != null) 
        {
            _topBar.Opacity = 0;
            _topBar.IsHitTestVisible = false;
        }
        if (_bottomBar != null) 
        {
            _bottomBar.Opacity = 0;
            _bottomBar.IsHitTestVisible = false;
        }
        Cursor = s_noneCursor;
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        if (DataContext is PlayerViewModel { AutoHideControls: false })
            return;

        if (DataContext is PlayerViewModel vm && !vm.IsPlaying && !string.IsNullOrEmpty(vm.VideoUrl))
        {
            // Do not hide controls if paused and video is loaded
            return;
        }
        HideControls();
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        ShowControls();
    }

    private void OnGridPointerExited(object? sender, PointerEventArgs e)
    {
        // When mouse leaves the window, hide controls immediately if playing
        if (DataContext is PlayerViewModel vm && vm.AutoHideControls && (vm.IsPlaying || string.IsNullOrEmpty(vm.VideoUrl)))
        {
            _hideTimer.Stop();
            HideControls();
        }
    }

    private void OnTimelinePointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control timeline || DataContext is not PlayerViewModel vm || vm.Duration <= 0)
            return;

        var now = DateTime.UtcNow;
        var timelinePos = e.GetPosition(timeline);
        var ratio = Math.Clamp(timelinePos.X / Math.Max(1, timeline.Bounds.Width), 0, 1);
        var previewTime = ratio * vm.Duration;

        if ((now - _lastTimelinePreviewAt).TotalMilliseconds < 80 &&
            Math.Abs(previewTime - _lastTimelinePreviewTime) < 1)
            return;

        _lastTimelinePreviewAt = now;
        _lastTimelinePreviewTime = previewTime;

        var bottomPos = _bottomBar != null ? e.GetPosition(_bottomBar) : timelinePos;
        var maxLeft = Math.Max(8, (_bottomBar?.Bounds.Width ?? Bounds.Width) - 244);
        var previewLeft = Math.Clamp(bottomPos.X - 118, 8, maxLeft);
        vm.ShowTimelinePreview(previewTime, previewLeft);
        ShowControls();
    }

    private void OnTimelinePointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.HideTimelinePreview();
    }

    // ──────────────────────────────────────────────────────────
    // Overlay positioning
    // ──────────────────────────────────────────────────────────

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateOverlayPosition();
        Focus();
    }

    private void UpdateOverlayPosition()
    {
        if (_ownerWindow == null) return;

        var clientPos = _ownerWindow.PointToScreen(new Point(0, 0));
        Position = clientPos;

        var clientSize = _ownerWindow.ClientSize;
        Width  = clientSize.Width;
        Height = clientSize.Height;
    }

    // ──────────────────────────────────────────────────────────
    // Window controls
    // ──────────────────────────────────────────────────────────

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ownerWindow.Close();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        _ownerWindow.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        OnFullscreenClick(sender, e);
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e)
    {
        _ownerWindow.WindowState = _ownerWindow.WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowSettingsOverlay();
    }

    private void OnScreenshotButtonPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        switch (properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonReleased:
                e.Handled = true;
                vm.TakeScreenshot(includeSubtitles: false);
                ShowControls();
                break;
            case PointerUpdateKind.RightButtonReleased:
                e.Handled = true;
                vm.TakeScreenshot(includeSubtitles: true);
                ShowControls();
                break;
        }
    }

    private void OnSubtitlePositionButtonPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        switch (properties.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonReleased:
                e.Handled = true;
                vm.MoveSubtitleUp();
                ShowControls();
                break;
            case PointerUpdateKind.RightButtonReleased:
                e.Handled = true;
                vm.MoveSubtitleDown();
                ShowControls();
                break;
        }
    }

    private void OnTrackMenuItemClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { DataContext: TrackInfo track } || DataContext is not PlayerViewModel vm)
            return;

        vm.SelectTrackCommand.Execute(track);

        var flyoutButtonName = track.Type == "sub" ? "SubtitleButton" : "AudioButton";
        this.FindControl<Button>(flyoutButtonName)?.Flyout?.Hide();
        ShowControls();
    }
}
