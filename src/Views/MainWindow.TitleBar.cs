using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Serilog;

namespace Kiriha.Views;

public partial class MainWindow
{
    private const double TitleBarHeight = 34;

    /// <summary>
    /// Drives window dragging and double-click maximize on the custom title
    /// bar. Hooked on the root Grid in MainWindow.axaml — relies on event
    /// bubbling: clicks that land on a Button/ListBoxItem inside the bar are
    /// already marked Handled by those controls and never reach us, so only
    /// "empty" title-bar pixels trigger a move.
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        if (point.Position.Y > TitleBarHeight) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    private void SetNativeTitle(string title)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero) SetWindowTextW(hwnd, title);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MainWindow: failed to set native window title");
        }
    }
}
