using System;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using Kiriha.Services.AppLifecycle;
using Material.Icons.Avalonia;

namespace Kiriha;

public partial class App : Application
{
    private IServiceProvider _serviceProvider = null!;
    private AppStartupCoordinator? _startupCoordinator;
    private PlayerModeCoordinator? _playerModeCoordinator;
    private ShutdownCoordinator? _shutdownCoordinator;
    private TrayService? _trayService;

    /// <summary>
    /// Process-wide DI root. Prefer constructor injection wherever possible;
    /// this static accessor exists strictly for places that Avalonia constructs
    /// without DI participation (XAML-instantiated <c>Window</c> code-behind
    /// and a handful of static helpers). It is consciously the only escape
    /// hatch - do NOT add new call sites in ViewModels or services.
    /// </summary>
    public static IServiceProvider Services
    {
        get
        {
            if (Current is not App app)
                throw new InvalidOperationException("App is not initialized");

            var sp = app._serviceProvider;
            if (sp == null)
                throw new InvalidOperationException("Service provider is not yet built (called before OnFrameworkInitializationCompleted)");

            return sp;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppStartupCoordinator.InstallUnhandledExceptionHandler();

        var args = Environment.GetCommandLineArgs();
        var isPlayerMode = PlayerModeCoordinator.IsPlayerMode(args);

        _serviceProvider = AppStartupCoordinator.BuildServiceProvider(isPlayerMode);
        _shutdownCoordinator = new ShutdownCoordinator(_serviceProvider);
        _trayService = new TrayService(this, _serviceProvider, _shutdownCoordinator);

        if (isPlayerMode)
        {
            _playerModeCoordinator = new PlayerModeCoordinator(this, _serviceProvider, _trayService);
            _playerModeCoordinator.Initialize(args);

            base.OnFrameworkInitializationCompleted();
            _trayService.DisableTrayIcons();
            return;
        }

        _startupCoordinator = new AppStartupCoordinator(this, _serviceProvider, _trayService, _shutdownCoordinator);
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
