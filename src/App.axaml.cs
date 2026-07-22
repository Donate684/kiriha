using System;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using Kiriha.Services.AppLifecycle;
using Material.Icons.Avalonia;

namespace Kiriha;

public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    private AppStartupCoordinator? _startupCoordinator;
    private PlayerModeCoordinator? _playerModeCoordinator;
    private ShutdownCoordinator? _shutdownCoordinator;
    private TrayService? _trayService;



    public override void OnFrameworkInitializationCompleted()
    {
        AppStartupCoordinator.InstallUnhandledExceptionHandler();

        var args = Environment.GetCommandLineArgs();
        var isPlayerMode = PlayerModeCoordinator.IsPlayerMode(args);

        ServiceProvider = AppStartupCoordinator.BuildServiceProvider(isPlayerMode);
        _shutdownCoordinator = new ShutdownCoordinator(ServiceProvider);
        _trayService = new TrayService(this, ServiceProvider, _shutdownCoordinator);

        if (isPlayerMode)
        {
            _playerModeCoordinator = new PlayerModeCoordinator(this, ServiceProvider, _trayService);
            _playerModeCoordinator.Initialize(args);

            base.OnFrameworkInitializationCompleted();
            _trayService.DisableTrayIcons();
            return;
        }

        _startupCoordinator = new AppStartupCoordinator(this, ServiceProvider, _trayService, _shutdownCoordinator);
        _startupCoordinator.Initialize(args);

        base.OnFrameworkInitializationCompleted();
    }

    public override void Initialize()
    {
        if (PlayerModeCoordinator.IsPlayerMode(Environment.GetCommandLineArgs()))
        {
            Styles.Add(new FluentTheme());
            Styles.Add(new MaterialIconStyles(null));
            return;
        }

        AvaloniaXamlLoader.Load(this);
    }

    public void UpdateTrayMenu() => _trayService?.UpdateTrayMenu();

    private void TrayRestore_Click(object? sender, EventArgs e) => _trayService?.RestoreMainWindow();

    private void TrayExit_Click(object? sender, EventArgs e) => _trayService?.Exit();
}
