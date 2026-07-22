using System.Linq;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Kiriha.ViewModels.Player;

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

        Focus();
    }

    private bool IsSettingsOverlayVisible()
    {
        return _settingsOverlayBackdrop?.IsVisible == true;
    }

    // ──────────────────────────────────────────────────────────
    // Slider scrubbing
    // ──────────────────────────────────────────────────────────
}
