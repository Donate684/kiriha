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
    private void OnSettingsBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        HideSettingsOverlay();
    }

    private void OnSettingsPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnSettingsCloseClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        HideSettingsOverlay();
    }

    private async void OnChooseScreenshotDirectoryClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not PlayerViewModel vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка для скриншотов",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        var path = folder?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            vm.ScreenshotDirectory = path;
    }

    private void ShowSettingsOverlay()
    {
        if (this.FindControl<Border>("SettingsOverlayBackdrop") is { } backdrop)
            backdrop.IsVisible = true;

        ShowControls();
    }

    private void HideSettingsOverlay()
    {
        if (this.FindControl<Border>("SettingsOverlayBackdrop") is { } backdrop)
            backdrop.IsVisible = false;
    }

    private bool IsSettingsOverlayVisible()
    {
        return this.FindControl<Border>("SettingsOverlayBackdrop")?.IsVisible == true;
    }

    // ──────────────────────────────────────────────────────────
    // Slider scrubbing
    // ──────────────────────────────────────────────────────────
}
