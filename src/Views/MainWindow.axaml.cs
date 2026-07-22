using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Views.Player;
using Serilog;

namespace Kiriha.Views;

public partial class MainWindow : KirihaWindowBase
{


    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(SettingsService settingsService) : this()
    {
        SettingsService = settingsService;
        ApplyMica();
        RestorePlacement();
        PositionChanged += OnWindowPositionChanged;
    }



    public void ApplyMica()
    {
        // Force transparency hints for testing, ignoring the check for a moment if needed
        var settings = SettingsService?.Current;
        if (settings == null) return;
        if (settings.UI.EnableMica)
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur };
            Background = null;
        }
        else
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            ClearValue(BackgroundProperty);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Mica is already applied in the constructor; re-applying here forces a
        // composition target recreate on the first frame, causing visible jank
        // under WinUI Composition. Only re-apply when the user toggles the
        // setting at runtime (handled elsewhere).

        // Now that the platform impl exists, Screens is populated. Push the
        // window back onto a visible monitor if the saved position points at a
        // display that's no longer connected (laptop dock, RDP, …).
        EnsureOnScreen();

        // Set the OS-level window text directly so taskbar peek / Alt-Tab /
        // Task Manager show "Kiriha". Going through Window.Title would also
        // make Avalonia render the string into the extended client area
        // (visually overlapping our custom sidebar branding). WM_SETTEXT
        // bypasses the Avalonia binding — Avalonia keeps Title="" and won't
        // overwrite our native value because the property never changes.
        SetNativeTitle("Kiriha");
    }



    public bool ForceExit { get; set; } = false;
    private bool _shutdownDispatched;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Persist whichever placement we have right now. Always — we want the
        // size to survive both real exit and hide-to-tray.
        SavePlacement();

        // Never hide-to-tray when the OS itself is shutting down or the application is exiting:
        bool isSystemShutdown = e.CloseReason == WindowCloseReason.OSShutdown
                             || e.CloseReason == WindowCloseReason.ApplicationShutdown;

        if (!ForceExit && !isSystemShutdown && SettingsService?.Current.System.CloseToTray == true)
        {
            e.Cancel = true;
            this.Hide();
        }
        else
        {
            ClosePlayerWindows();
            base.OnClosing(e);
            if (!ForceExit && !isSystemShutdown && !_shutdownDispatched)
            {
                _shutdownDispatched = true;
                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
                }
            }
        }
    }

    private void ClosePlayerWindows()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                return;

            foreach (var playerWindow in desktop.Windows.OfType<PlayerWindow>().ToArray())
            {
                if (playerWindow.IsVisible)
                    playerWindow.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MainWindow: failed to close player windows before shutdown");
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            if (WindowState == WindowState.Minimized)
            {

                if (SettingsService?.Current.System.MinimizeToTray == true)
                {
                    this.Hide();
                }
            }
        }
        else if (change.Property == ClientSizeProperty || change.Property == BoundsProperty)
        {
            // Mirror outer-frame size into our last-known-good state. Skip
            // Minimized (zero-size) and Maximized (covers full work area) so
            // the saved value is always something useful to restore back to.
            if (WindowState == WindowState.Normal && Bounds.Width > 0 && Bounds.Height > 0)
            {
                _lastNormalWidth = Bounds.Width;
                _lastNormalHeight = Bounds.Height;
            }
        }
        else if (change.Property == IsVisibleProperty)
        {
            if (!(bool)change.NewValue!)
            {
                // Window hidden (e.g. to tray) - persist the latest geometry
                // so a later `--minimized` launch (or a tray restore) brings
                // the window back at exactly the same size and place.
                SavePlacement();
            }
        }
    }


}
