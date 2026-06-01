using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Kiriha.Composition;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.ViewModels;
using Kiriha.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Services.AppLifecycle;

public sealed class AppStartupCoordinator
{
    private readonly Application _app;
    private readonly IServiceProvider _serviceProvider;
    private readonly TrayService _trayService;
    private readonly ShutdownCoordinator _shutdownCoordinator;
    private readonly Stopwatch _startupStopwatch = Stopwatch.StartNew();

    public AppStartupCoordinator(
        Application app,
        IServiceProvider serviceProvider,
        TrayService trayService,
        ShutdownCoordinator shutdownCoordinator)
    {
        _app = app;
        _serviceProvider = serviceProvider;
        _trayService = trayService;
        _shutdownCoordinator = shutdownCoordinator;
    }

    public static void InstallUnhandledExceptionHandler()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (e.Exception is ObjectDisposedException ode
                && ode.ObjectName != null
                && ode.ObjectName.Contains("Ref<Avalonia.Platform.IBitmapImpl>"))
            {
                Log.Warning(ode, "Swallowed AsyncImageLoader disposed-bitmap race in layout pass");
                e.Handled = true;
                return;
            }

            Log.Fatal(e.Exception, "Unhandled UI thread exception");
            CrashReporter.WriteCrash(e.Exception, "Dispatcher.UIThread.UnhandledException");
        };
    }

    public static IServiceProvider BuildServiceProvider(bool isPlayerMode)
    {
        var sw = Stopwatch.StartNew();
        var services = new ServiceCollection();
        if (isPlayerMode)
            ConfigurePlayerServices(services);
        else
            ConfigureServices(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
#if DEBUG
            ValidateOnBuild = !isPlayerMode,
#endif
            ValidateScopes = true,
        });
        Log.Information(
            "StartupTiming: service provider built mode={Mode} elapsedMs={ElapsedMs}",
            isPlayerMode ? "player" : "app",
            sw.ElapsedMilliseconds);
        return provider;
    }

    public void Initialize(string[] args)
    {
        var sw = Stopwatch.StartNew();
        var settings = _serviceProvider.GetRequiredService<SettingsService>();
        Log.Information("StartupTiming: settings resolved elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        sw.Restart();
        _app.RequestedThemeVariant = settings.Current.UI.Theme switch
        {
            ThemeType.Light => ThemeVariant.Light,
            ThemeType.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        Log.Information("StartupTiming: theme applied elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        sw.Restart();
        var loc = _serviceProvider.GetRequiredService<LocalizationService>();
        loc.LoadLanguage(settings.Current.UI.LanguageCode);
        Log.Information("StartupTiming: localization loaded elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        sw.Restart();
        var imageCache = _serviceProvider.GetRequiredService<ImageCacheService>();
        ImageLoader.AsyncImageLoader = new KirihaImageLoader(imageCache);
        CachedImage.Initialize(imageCache);
        Log.Information("StartupTiming: image services initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);

        if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += _shutdownCoordinator.OnShutdownRequested;
            InitializeMainWindow(desktop, settings, args);
        }

        _trayService.UpdateTrayMenu();
        Log.Information("StartupTiming: app coordinator initialized elapsedMs={ElapsedMs}", _startupStopwatch.ElapsedMilliseconds);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        PathHelper.EnsureDirectoriesExist();

        services.AddSingleton<IBackgroundTaskSupervisor, BackgroundTaskSupervisor>();

        services
            .AddKirihaData(PathHelper.GetDbPath())
            .AddKirihaTracking()
            .AddKirihaUi();
    }

    private static void ConfigurePlayerServices(IServiceCollection services)
    {
        PathHelper.EnsureDirectoriesExist();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IPlayerMediaMetadataResolver, FilenamePlayerMediaMetadataResolver>();
    }

    private void InitializeMainWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        SettingsService settings,
        string[] args)
    {
        if (settings.NeedsFirstStartup())
        {
            var setupVm = _serviceProvider.GetRequiredService<FirstStartupViewModel>();
            setupVm.SetupCompleted += OnSetupCompleted;

            desktop.MainWindow = new FirstStartupWindow { DataContext = setupVm };
            return;
        }

        var mainWindowVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        desktop.MainWindow = new MainWindow { DataContext = mainWindowVm };

        var startMinimized = args.Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var hideToTrayOnStart = startMinimized && settings.Current.System.MinimizeToTray;

        if (startMinimized)
        {
            desktop.MainWindow.ShowInTaskbar = !hideToTrayOnStart;
            desktop.MainWindow.WindowState = WindowState.Minimized;
        }

        desktop.MainWindow.Loaded += (_, _) =>
        {
            Log.Information("StartupTiming: main window loaded elapsedMs={ElapsedMs}", _startupStopwatch.ElapsedMilliseconds);
            Dispatcher.UIThread.Post(
                () => Log.Information("StartupTiming: first render-priority callback elapsedMs={ElapsedMs}", _startupStopwatch.ElapsedMilliseconds),
                DispatcherPriority.Render);

            Dispatcher.UIThread.Post(async () =>
            {
                await InitializeAppServicesAsync();
                if (hideToTrayOnStart)
                    desktop.MainWindow!.Hide();
            }, DispatcherPriority.Background);
        };
    }

    private async void OnSetupCompleted()
    {
        try
        {
            if (_app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is FirstStartupWindow setupWindow)
            {
                if (setupWindow.DataContext is FirstStartupViewModel setupVm)
                    setupVm.SetupCompleted -= OnSetupCompleted;

                var mainWindowVm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
                var main = new MainWindow { DataContext = mainWindowVm };
                main.Show();
                desktop.MainWindow = main;
                setupWindow.Close();

                await InitializeAppServicesAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "App: OnSetupCompleted failed; app will continue but services may be in an inconsistent state.");
        }
    }

    private async Task InitializeAppServicesAsync()
    {
        await _serviceProvider.GetRequiredService<AppReadinessService>().StartAsync();
        ShowPendingCrashReport();
    }

    private void ShowPendingCrashReport()
    {
        try
        {
            var pending = CrashReporter.GetPendingCrashFile();
            if (string.IsNullOrEmpty(pending))
                return;

            if (_app.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var crashWindow = new CrashReportWindow
                    {
                        DataContext = new CrashReportViewModel(pending)
                    };

                    if (desktop.MainWindow != null && desktop.MainWindow.IsVisible)
                        crashWindow.Show(desktop.MainWindow);
                    else
                        crashWindow.Show();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ShowPendingCrashReport: window show failed");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShowPendingCrashReport failed");
        }
    }
}
