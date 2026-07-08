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
        if (_settingsOverlayBackdrop != null)
            _settingsOverlayBackdrop.IsVisible = true;

        if (DataContext is PlayerViewModel vm)
            vm.SetMpvRuntimeDiagnosticsVisible(true);

        ShowControls();
    }

    private void HideSettingsOverlay()
    {
        if (_settingsOverlayBackdrop != null)
            _settingsOverlayBackdrop.IsVisible = false;

        if (DataContext is PlayerViewModel vm)
            vm.SetMpvRuntimeDiagnosticsVisible(false);
    }

    private bool IsSettingsOverlayVisible()
    {
        return _settingsOverlayBackdrop?.IsVisible == true;
    }

    // ──────────────────────────────────────────────────────────
    // Slider scrubbing
    // ──────────────────────────────────────────────────────────
}
