using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kiriha.Services.Data;

namespace Kiriha.Views;

public partial class AnimeDetailsWindow : KirihaWindowBase
{
    public AnimeDetailsWindow()
    {
        InitializeComponent();
    }

    public AnimeDetailsWindow(SettingsService settingsService) : this()
    {
        SettingsService = settingsService;
        ApplyMica();
        Opened += OnOpened;
    }

    private void ShareRow_Click(object? sender, RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ShareMainButton");
        btn?.Flyout?.Hide();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // High-DPI safety + owner-aware centring. The XAML asks for 880x680 DIP
        // and WindowStartupLocation=CenterOwner, but two things can break that:
        //
        //   1) On a 2560x1440 display at 200% scaling, 680 DIP = 1360 px which
        //      is taller than a typical work area (~1400 px) once we add the
        //      40 px safety margin. Without shrinking, Windows force-stretches
        //      or clips the window.
        //
        //   2) When this dialog is taller than the MainWindow (which is itself
        //      clamped to fit the work area on hi-DPI displays), CenterOwner
        //      computes a negative top and the OS pins us to y=0, producing
        //      the "always glued to the top" symptom from the bug report.
        //
        // We always recompute size+position once Avalonia knows which screen
        // we're on, then clamp inside the work area so the title bar is
        // guaranteed reachable regardless of owner geometry.
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var workArea = screen.WorkingArea;

        // 1. Shrink to fit the work area (40 px margin on every side).
        const int marginPx = 40;
        var maxWDip = Math.Max(200, (workArea.Width - marginPx * 2) / scale);
        var maxHDip = Math.Max(150, (workArea.Height - marginPx * 2) / scale);
        if (Width > maxWDip) Width = maxWDip;
        if (Height > maxHDip) Height = maxHDip;

        // 2. Compute the actual window footprint in physical pixels.
        var winWPx = (int)Math.Ceiling(Width * scale);
        var winHPx = (int)Math.Ceiling(Height * scale);

        // 3. Centre on owner when we can (so the user keeps the spatial link
        //    between the list item they clicked and the popup), otherwise
        //    centre on the screen as a graceful fallback.
        int x, y;
        if (Owner is Window owner && owner.IsVisible)
        {
            var ownerWPx = (int)(owner.Bounds.Width * scale);
            var ownerHPx = (int)(owner.Bounds.Height * scale);
            x = owner.Position.X + (ownerWPx - winWPx) / 2;
            y = owner.Position.Y + (ownerHPx - winHPx) / 2;
        }
        else
        {
            x = workArea.X + (workArea.Width - winWPx) / 2;
            y = workArea.Y + (workArea.Height - winHPx) / 2;
        }

        // 4. Clamp inside the work area so we never end up pinned to y=0 just
        //    because the owner is shorter than this dialog.
        x = Math.Clamp(x, workArea.X, workArea.X + Math.Max(0, workArea.Width - winWPx));
        y = Math.Clamp(y, workArea.Y, workArea.Y + Math.Max(0, workArea.Height - winHPx));

        Position = new PixelPoint(x, y);
    }

    public void ApplyMica()
    {
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

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void OnTitleBarPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }
}
