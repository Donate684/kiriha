using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Views;

namespace Kiriha.Services.AppLifecycle;

public sealed class TrayService
{
    private readonly Application _app;
    private readonly IServiceProvider _serviceProvider;
    private readonly ShutdownCoordinator _shutdownCoordinator;

    public TrayService(Application app, IServiceProvider serviceProvider, ShutdownCoordinator shutdownCoordinator)
    {
        _app = app;
        _serviceProvider = serviceProvider;
        _shutdownCoordinator = shutdownCoordinator;
    }

    public void DisableTrayIcons()
    {
        var icons = TrayIcon.GetIcons(_app);
        if (icons == null)
            return;

        foreach (var icon in icons.ToArray())
        {
            icon.IsVisible = false;
            icon.Dispose();
        }

        icons.Clear();
    }

    public void UpdateTrayMenu()
    {
        var icons = TrayIcon.GetIcons(_app);
        if (icons == null || icons.Count == 0)
            return;

        var tray = icons[0];
        if (tray.Menu == null)
            return;

        tray.ToolTipText = _app.Resources["l.common.app_name"] as string;
        if (tray.Menu.Items.Count >= 3)
        {
            if (tray.Menu.Items[0] is NativeMenuItem restore)
                restore.Header = _app.Resources["l.navigation.tray.restore"] as string;

            if (tray.Menu.Items[2] is NativeMenuItem exit)
                exit.Header = _app.Resources["l.navigation.tray.exit"] as string;
        }
    }

    public void RestoreMainWindow()
    {
        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is MainWindow main)
        {
            main.ShowInTaskbar = true;
            main.Show();
            main.WindowState = WindowState.Normal;
            main.Activate();
        }
    }

    public async void Exit()
    {
        if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        await _shutdownCoordinator.DrainAsync();

        if (desktop.MainWindow is MainWindow main)
        {
            main.ForceExit = true;
            main.Close();
        }

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        desktop.Shutdown();
    }
}
