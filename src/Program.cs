using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Kiriha.Core.Player;
using Serilog;
using Velopack;

namespace Kiriha;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Kiriha.Core.Platform.PathHelper.EnsureDirectoriesExist();

        bool isPlayer = Array.Exists(args, arg => arg.Equals("--player", StringComparison.OrdinalIgnoreCase));

        bool createdNew = true;
        System.Threading.Mutex? mutex = isPlayer
            ? null
            : new System.Threading.Mutex(true, Kiriha.Core.Constants.System.MutexName, out createdNew);
        System.Threading.Mutex? playerMutex = null;

        try
        {
            if (isPlayer)
            {
                playerMutex = new System.Threading.Mutex(true, Kiriha.Core.Player.PlayerProcessBridge.MutexName, out var playerCreatedNew);
                if (!playerCreatedNew)
                {
                    Kiriha.Core.Player.PlayerProcessBridge.TryForward(args);
                    return;
                }
            }

            if (!createdNew)
            {
                try
                {
                    using var client = new System.IO.Pipes.NamedPipeClientStream(".", "Kiriha.InstanceServer", System.IO.Pipes.PipeDirection.Out);
                    client.Connect(1000);
                    using var writer = new System.IO.StreamWriter(client);
                    writer.WriteLine(PipeArgumentSerializer.Serialize(args));
                }
                catch (Exception ex) { Console.WriteLine("Failed to forward arguments: " + ex.Message); }

                // Logger is not configured yet - write to console for diagnostics.
                Console.WriteLine("Another instance is already running. Arguments forwarded. Exiting.");
                return;
            }

            bool enableLogging = false;
            try
            {
                var settingsPath = Kiriha.Core.Platform.PathHelper.GetSettingsPath();
                if (File.Exists(settingsPath))
                {
                    var content = File.ReadAllText(settingsPath);
                    if (content.Contains("\"EnableLogging\": true", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("\"EnableLogging\":true", StringComparison.OrdinalIgnoreCase))
                    {
                        enableLogging = true;
                    }
                }
            }
            catch { }

            string logTemplate = Path.Combine(Kiriha.Core.Platform.PathHelper.GetLogsPath(), "kiriha-.txt");

            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console();

            if (enableLogging)
            {
                loggerConfig.WriteTo.File(logTemplate, rollingInterval: RollingInterval.Day);
            }

            Log.Logger = loggerConfig.CreateLogger();

            Log.Information(Kiriha.Core.Constants.System.AppStartedLog);
            // Mask OAuth callback parameters (code, token, refresh) before logging command-line args.
            Log.Information("Arguments: {Args}", MaskSensitiveArgs(args));

            try
            {
                // Velopack startup logic
                VelopackApp.Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Velopack startup error!");
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
            {
                var ex = eventArgs.ExceptionObject as Exception;
                Log.Fatal(ex, "Critical Error (UnhandledException)! Terminating={Terminating}", eventArgs.IsTerminating);

                if (eventArgs.IsTerminating)
                    Kiriha.Core.Infrastructure.CrashReporter.WriteCrash(ex, "AppDomain.UnhandledException");
            };

            TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
            {
                Log.Warning(eventArgs.Exception, "Unobserved task exception (non-fatal, swallowed)");
                eventArgs.SetObserved();
            };

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Critical Error during application startup or execution!");
                Kiriha.Core.Infrastructure.CrashReporter.WriteCrash(ex, "Program.Main");
                throw;
            }
        }
        finally
        {
            Log.CloseAndFlush();
            playerMutex?.Dispose();
            mutex?.Dispose();
        }
    }

    private static readonly string[] SensitiveQueryKeys = { "code", "token", "access_token", "refresh_token", "client_secret" };
    private static readonly char[] SensitiveArgSeparators = { '&', ' ', '?', '#' };

    /// <summary>
    /// Masks OAuth-sensitive query parameters in command-line arguments so they don't
    /// leak into Serilog files. The OS sometimes routes OAuth callbacks via the
    /// command line (custom URI schemes), and full URLs would otherwise hit disk.
    /// </summary>
    private static string MaskSensitiveArgs(string[] args)
    {
        if (args == null || args.Length == 0) return string.Empty;
        var masked = new string[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i] ?? string.Empty;
            foreach (var key in SensitiveQueryKeys)
            {
                var idx = a.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                var end = a.IndexOfAny(SensitiveArgSeparators, idx);
                if (end < 0) end = a.Length;
                a = a.Substring(0, idx + key.Length + 1) + "***" + (end < a.Length ? a.Substring(end) : "");
            }
            masked[i] = a;
        }
        return string.Join(" ", masked);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new Win32PlatformOptions
            {
                CompositionMode = new[] { Win32CompositionMode.WinUIComposition }
            })
            .LogToTrace();
}
