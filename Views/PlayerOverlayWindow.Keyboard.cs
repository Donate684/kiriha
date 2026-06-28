using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kiriha.Core.Mpv;
using Kiriha.ViewModels;

namespace Kiriha.Views;

public partial class PlayerOverlayWindow
{
    private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsTextInputSource(e.Source) || DataContext is not PlayerViewModel vm)
            return;

        if (IsSettingsOverlayVisible() && e.Key == Key.Escape)
        {
            e.Handled = true;
            HideSettingsOverlay();
            return;
        }

        if (MatchesHotkey(e, vm.ScreenshotWithSubtitlesHotkey))
        {
            e.Handled = true;
            vm.TakeScreenshot(includeSubtitles: true);
            return;
        }

        if (MatchesHotkey(e, vm.ScreenshotWithoutSubtitlesHotkey))
        {
            e.Handled = true;
            vm.TakeScreenshot(includeSubtitles: false);
            return;
        }

        if (MatchesHotkey(e, vm.SubtitleStyleHotkey))
        {
            e.Handled = true;
            vm.ToggleSubtitleStyleOverride();
            return;
        }

        var actualModifiers = e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);
        if (e.Key == Key.R && actualModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            vm.MoveSubtitleUp();
            return;
        }

        if (e.Key == Key.R && actualModifiers == KeyModifiers.Shift)
        {
            e.Handled = true;
            vm.MoveSubtitleDown();
            return;
        }

        if (MatchesHotkey(e, vm.VolumeUpHotkey))
        {
            e.Handled = true;
            vm.AdjustVolume(1);
            return;
        }

        if (MatchesHotkey(e, vm.VolumeDownHotkey))
        {
            e.Handled = true;
            vm.AdjustVolume(-1);
            return;
        }

        if (MatchesHotkey(e, vm.SeekBackwardHotkey))
        {
            e.Handled = true;
            vm.SeekRelative(-1);
            return;
        }

        if (MatchesHotkey(e, vm.SeekForwardHotkey))
        {
            e.Handled = true;
            vm.SeekRelative(1);
            return;
        }

        if (MatchesHotkey(e, vm.ReloadSubtitlesHotkey))
        {
            e.Handled = true;
            vm.ReloadSubtitlesCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.FrameStepForwardHotkey))
        {
            e.Handled = true;
            vm.FrameStepForwardCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.FrameStepBackwardHotkey))
        {
            e.Handled = true;
            vm.FrameStepBackwardCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.TogglePlayPauseHotkey))
        {
            e.Handled = true;
            vm.TogglePlayPauseCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.ToggleFullscreenHotkey))
        {
            e.Handled = true;
            OnFullscreenClick(null, new RoutedEventArgs());
            return;
        }

        if (MatchesHotkey(e, vm.ExitFullscreenHotkey) && _ownerWindow.WindowState == WindowState.FullScreen)
        {
            e.Handled = true;
            _ownerWindow.WindowState = WindowState.Normal;
            return;
        }

        if (MatchesHotkey(e, vm.ToggleMuteHotkey))
        {
            e.Handled = true;
            vm.ToggleMuteCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.CycleAudioHotkey))
        {
            e.Handled = true;
            vm.CycleAudioCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.CycleSubtitleHotkey))
        {
            e.Handled = true;
            vm.CycleSubtitleCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.NextMediaHotkey))
        {
            e.Handled = true;
            vm.OpenNextMediaCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.PreviousMediaHotkey))
        {
            e.Handled = true;
            vm.OpenPreviousMediaCommand.Execute(null);
            return;
        }

        if (MatchesHotkey(e, vm.SpeedDownHotkey))
        {
            e.Handled = true;
            vm.AdjustPlaybackSpeed(-0.25);
            return;
        }

        if (MatchesHotkey(e, vm.SpeedUpHotkey))
        {
            e.Handled = true;
            vm.AdjustPlaybackSpeed(0.25);
        }
    }

    private static bool IsTextInputSource(object? source)
    {
        if (source is not Visual visual)
            return false;

        return visual.GetVisualAncestors().Prepend(visual).Any(x =>
            x is TextBox ||
            x is NumericUpDown ||
            x is ComboBox);
    }

    private static bool IsPlayerControlSource(object? source)
    {
        if (source is not Visual visual)
            return false;

        return visual.GetVisualAncestors().Prepend(visual).Any(x =>
            x is Button ||
            x is Slider ||
            x is Thumb ||
            x is ToggleSwitch ||
            x is ComboBox ||
            x is TextBox ||
            x is NumericUpDown ||
            x is ScrollViewer ||
            x is ListBox);
    }

    private static bool MatchesHotkey(KeyEventArgs e, string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        if (!TryParseHotkey(hotkey, out var key, out var modifiers))
            return false;

        var actualModifiers = e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta);
        return e.Key == key && actualModifiers == modifiers;
    }

    private static bool TryParseHotkey(string hotkey, out Key key, out KeyModifiers modifiers)
    {
        key = Key.None;
        modifiers = KeyModifiers.None;

        foreach (var rawPart in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Shift;
            else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Alt;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Meta", StringComparison.OrdinalIgnoreCase))
                modifiers |= KeyModifiers.Meta;
            else if (Enum.TryParse(part, ignoreCase: true, out Key parsedKey))
                key = parsedKey;
            else
                return false;
        }

        return key != Key.None;
    }
}
