using System;
using Avalonia;
using Avalonia.Controls;
using Kiriha.Models;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Views;

public partial class MainWindow
{
    // Defaults used when the user has never resized the window before.
    // Match the design-time hints in MainWindow.axaml so layout stays sane.
    private const double DefaultWidth = 1100;
    private const double DefaultHeight = 720;

    // Cached "normal-state" geometry. We never persist values captured while
    // the window is Minimized/Maximized — those would clobber the size the
    // user actually wants to come back to.
    private double _lastNormalWidth = DefaultWidth;
    private double _lastNormalHeight = DefaultHeight;
    private PixelPoint? _lastNormalPosition;

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _lastNormalPosition = Position;
        }
    }

    /// <summary>
    /// Applies the previously persisted size/position/maximized state, falling
    /// back to centred defaults on first run. Called once from the constructor
    /// — before the window is shown — so Avalonia honours the values without
    /// any visible "resize after open" jank.
    /// </summary>
    private void RestorePlacement()
    {
        try
        {
            var placement = SettingsService?.Current.UI.Window
                            ?? new AppSettings.WindowPlacement();

            var width = placement.Width > 0 ? placement.Width : DefaultWidth;
            var height = placement.Height > 0 ? placement.Height : DefaultHeight;

            // Hard floor against corrupted settings shrinking the window to
            // a 0×0 strip. Doesn't restrict the user's manual resize at
            // runtime — XAML has no MinWidth/MinHeight, so the live window is
            // freely resizable down to whatever the OS allows.
            const double safetyFloorWidth = 200;
            const double safetyFloorHeight = 150;
            if (width < safetyFloorWidth) width = safetyFloorWidth;
            if (height < safetyFloorHeight) height = safetyFloorHeight;

            Width = width;
            Height = height;
            _lastNormalWidth = width;
            _lastNormalHeight = height;

            if (placement.X.HasValue && placement.Y.HasValue)
            {
                // Switch to manual placement only when we actually have a
                // saved position — otherwise the XAML-declared CenterScreen
                // behaviour kicks in for first-run users.
                WindowStartupLocation = WindowStartupLocation.Manual;
                var pt = new PixelPoint(placement.X.Value, placement.Y.Value);
                Position = pt;
                _lastNormalPosition = pt;
            }

            if (placement.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MainWindow: failed to restore window placement, using defaults");
        }
    }

    /// <summary>
    /// Validates the current window geometry against the actual screen layout
    /// and corrects two failure modes that would otherwise leave the user
    /// unable to drag the window:
    ///
    /// <list type="bullet">
    ///   <item><description>Saved position points at a screen that's no longer
    ///   connected — the title bar would be invisible. We re-centre on the
    ///   primary monitor.</description></item>
    ///   <item><description>Default 1100×720 DIPs doesn't fit the work area at
    ///   high DPI scaling (e.g. a 2560×1440 monitor at 200% gives only
    ///   ~1280×680 usable DIPs, so the bottom of the window — and the resize
    ///   grip — fall off the screen). We shrink the window to fit with a
    ///   safety margin and then re-centre.</description></item>
    /// </list>
    ///
    /// Avalonia exposes <see cref="Screen.WorkingArea"/> in physical pixels and
    /// <see cref="Screen.Scaling"/> as the DPI factor; <see cref="Window.Bounds"/>
    /// and <see cref="Window.Width"/>/<see cref="Window.Height"/> are in DIPs,
    /// so we always convert via <c>scaling</c> before comparing.
    /// </summary>
    private void EnsureOnScreen()
    {
        try
        {
            var screens = Screens;
            if (screens == null || screens.All == null || screens.All.Count == 0) return;

            // Pick the screen the window currently lives on; fall back to
            // primary if Avalonia can't decide (e.g. window is off-screen).
            var screen = screens.ScreenFromWindow(this)
                         ?? screens.Primary
                         ?? screens.All[0];

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var area = screen.WorkingArea;

            // Available room in DIPs, with a small margin so the window never
            // sits flush against the taskbar / screen edge.
            const int marginPx = 16;
            var availWidthDip = Math.Max(1, (area.Width - marginPx * 2) / scaling);
            var availHeightDip = Math.Max(1, (area.Height - marginPx * 2) / scaling);

            var desiredWidth = Bounds.Width > 0 ? Bounds.Width : Width;
            var desiredHeight = Bounds.Height > 0 ? Bounds.Height : Height;
            if (double.IsNaN(desiredWidth) || desiredWidth <= 0) desiredWidth = DefaultWidth;
            if (double.IsNaN(desiredHeight) || desiredHeight <= 0) desiredHeight = DefaultHeight;

            var clampedWidth = Math.Min(desiredWidth, availWidthDip);
            var clampedHeight = Math.Min(desiredHeight, availHeightDip);

            bool sizeChanged = Math.Abs(clampedWidth - desiredWidth) > 0.5
                            || Math.Abs(clampedHeight - desiredHeight) > 0.5;
            if (sizeChanged)
            {
                Width = clampedWidth;
                Height = clampedHeight;
                _lastNormalWidth = clampedWidth;
                _lastNormalHeight = clampedHeight;
            }

            // Now check whether the title bar is reachable. We require at
            // least 80px of the title bar to be inside the work area on both
            // axes — anything less and Windows hit-testing won't let the user
            // grab and move the window.
            var winWidthPx = (int)Math.Ceiling(clampedWidth * scaling);
            var winHeightPx = (int)Math.Ceiling(clampedHeight * scaling);
            var pos = Position;

            const int reachable = 80;
            bool offHorizontally = pos.X + winWidthPx <= area.X + reachable
                                || pos.X >= area.X + area.Width - reachable;
            bool offVertically = pos.Y <= area.Y - reachable
                              || pos.Y >= area.Y + area.Height - reachable;

            if (sizeChanged || offHorizontally || offVertically)
            {
                var centered = new PixelPoint(
                    area.X + Math.Max(0, (area.Width - winWidthPx) / 2),
                    area.Y + Math.Max(0, (area.Height - winHeightPx) / 2));
                Position = centered;
                _lastNormalPosition = centered;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MainWindow: failed to validate window placement against screens");
        }
    }

    /// <summary>
    /// Pushes the latest in-memory geometry into <see cref="AppSettings"/> and
    /// triggers a debounced save. Safe to call repeatedly (during resize, on
    /// hide, on close): the underlying <see cref="SettingsService.Save"/> is
    /// debounced so we don't write the JSON on every mouse-move.
    /// </summary>
    private void SavePlacement()
    {
        try
        {
            var settings = SettingsService;
            if (settings == null) return;
            settings.Update(current =>
            {
                var placement = current.UI.Window ??= new AppSettings.WindowPlacement();

                placement.Width = _lastNormalWidth;
                placement.Height = _lastNormalHeight;

                if (_lastNormalPosition.HasValue)
                {
                    placement.X = _lastNormalPosition.Value.X;
                    placement.Y = _lastNormalPosition.Value.Y;
                }

                // Only treat Maximized as a sticky state. Minimized is transient
                // (tray, taskbar) — restoring as Minimized on next launch would be
                // confusing.
                placement.Maximized = WindowState == WindowState.Maximized;
            }, SettingsSection.UI, save: false);

            // Synchronous flush, not the debounced Save(). SavePlacement is
            // only invoked at terminal moments (window closing, hide-to-tray)
            // so the 500 ms debounce window would race the process exit and
            // silently drop the just-resized geometry.
            settings.SaveImmediate();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MainWindow: failed to persist window placement");
        }
    }
}
