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
using Kiriha.Models;
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
    private void OnSliderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.BeginScrub();
    }

    private void OnSliderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is PlayerViewModel vm)
            vm.EndScrub();
    }

    // ──────────────────────────────────────────────────────────
    // Background click / drag
    // ──────────────────────────────────────────────────────────

    private DispatcherTimer? _leftClickTimer;

    private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsSettingsOverlayVisible())
        {
            e.Handled = true;
            HideSettingsOverlay();
            return;
        }

        if (DataContext is not PlayerViewModel vm)
            return;

        var properties = e.GetCurrentPoint(this).Properties;
        var action = PlayerMouseAction.None;
        if (properties.IsLeftButtonPressed)
            action = vm.LeftClickAction?.Value ?? PlayerMouseAction.TogglePlayPause;
        else if (properties.IsRightButtonPressed)
            action = vm.RightClickAction?.Value ?? PlayerMouseAction.OpenSettings;
        else if (properties.IsMiddleButtonPressed)
            action = vm.MiddleClickAction?.Value ?? PlayerMouseAction.ToggleFullscreen;

        if (action == PlayerMouseAction.None)
            return;

        e.Handled = true;

        if (properties.IsLeftButtonPressed)
        {
            _leftClickTimer?.Stop();

            _leftClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _leftClickTimer.Tick += (s, args) =>
            {
                _leftClickTimer?.Stop();
                _leftClickTimer = null;
                ExecuteMouseAction(vm, action);
            };
            _leftClickTimer.Start();
        }
        else
        {
            ExecuteMouseAction(vm, action);
        }
    }

    private void OnBackgroundDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_leftClickTimer != null)
        {
            _leftClickTimer.Stop();
            _leftClickTimer = null;
        }

        if (IsSettingsOverlayVisible())
        {
            e.Handled = true;
            HideSettingsOverlay();
            return;
        }

        if (DataContext is not PlayerViewModel vm)
            return;

        var action = PlayerMouseAction.ToggleFullscreen;
        
        e.Handled = true;
        ExecuteMouseAction(vm, action);
    }

    private void OnBackgroundPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsSettingsOverlayVisible() || DataContext is not PlayerViewModel vm)
            return;

        var action = e.Delta.Y > 0
            ? vm.WheelUpAction?.Value ?? PlayerWheelAction.VolumeUp
            : vm.WheelDownAction?.Value ?? PlayerWheelAction.VolumeDown;

        if (action == PlayerWheelAction.None)
            return;

        e.Handled = true;
        ExecuteWheelAction(vm, action);
        ShowControls();
    }

    private void OnTopBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        BeginOwnerMoveDrag(e);
    }

    private void OnBottomBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsSettingsOverlayVisible())
            return;

        if (IsPlayerControlSource(e.Source))
            return;

        e.Handled = true;
        BeginOwnerMoveDrag(e);
    }

    private void OnDragStripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsSettingsOverlayVisible())
            return;

        e.Handled = true;
        BeginOwnerMoveDrag(e);
    }

    private void BeginOwnerMoveDrag(PointerPressedEventArgs e)
    {
        Opacity = 0;
        try
        {
            _ownerWindow.BeginMoveDrag(e);
        }
        finally
        {
            UpdateOverlayPosition();
            Opacity = 1;
        }
    }

    private void ExecuteMouseAction(PlayerViewModel vm, PlayerMouseAction action)
    {
        switch (action)
        {
            case PlayerMouseAction.TogglePlayPause:
                vm.TogglePlayPauseCommand.Execute(null);
                break;
            case PlayerMouseAction.ToggleFullscreen:
                OnFullscreenClick(null, new RoutedEventArgs());
                break;
            case PlayerMouseAction.ShowControls:
                ShowControls();
                break;
            case PlayerMouseAction.OpenSettings:
                ShowSettingsOverlay();
                break;
            case PlayerMouseAction.SeekBackward10:
                vm.SkipCommand.Execute("-10");
                break;
            case PlayerMouseAction.SeekForward10:
                vm.SkipCommand.Execute("10");
                break;
            case PlayerMouseAction.CycleAudio:
                vm.CycleAudioCommand.Execute(null);
                break;
            case PlayerMouseAction.CycleSubtitle:
                vm.CycleSubtitleCommand.Execute(null);
                break;
            case PlayerMouseAction.None:
            default:
                break;
        }
    }

    private void ExecuteWheelAction(PlayerViewModel vm, PlayerWheelAction action)
    {
        var step = Math.Max(1, vm.WheelVolumeStep);

        switch (action)
        {
            case PlayerWheelAction.VolumeUp:
                vm.AdjustVolume(step);
                break;
            case PlayerWheelAction.VolumeDown:
                vm.AdjustVolume(-step);
                break;
            case PlayerWheelAction.SeekForward:
                vm.SeekRelative(10);
                break;
            case PlayerWheelAction.SeekBackward:
                vm.SeekRelative(-10);
                break;
            case PlayerWheelAction.SpeedUp:
                vm.PlaybackSpeed = Math.Clamp(vm.PlaybackSpeed + 0.25, 0.25, 2.0);
                break;
            case PlayerWheelAction.SpeedDown:
                vm.PlaybackSpeed = Math.Clamp(vm.PlaybackSpeed - 0.25, 0.25, 2.0);
                break;
            case PlayerWheelAction.None:
            default:
                break;
        }
    }

    // ──────────────────────────────────────────────────────────
    // Drag & Drop
    // ──────────────────────────────────────────────────────────
}
