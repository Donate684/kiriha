using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Kiriha.Core;
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
        Kiriha.Core.PathHelper.EnsureDirectoriesExist();

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
                playerMutex = new System.Threading.Mutex(true, Kiriha.Core.PlayerProcessBridge.MutexName, out var playerCreatedNew);
                if (!playerCreatedNew)
                {
                    Kiriha.Core.PlayerProcessBridge.TryForward(args);
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
                catch { }

                // Logger is not configured yet - write to console for diagnostics.
                Console.WriteLine("Another instance is already running. Arguments forwarded. Exiting.");
                return;
            }

            string logTemplate = Path.Combine(Kiriha.Core.PathHelper.GetLogsPath(), "kiriha-.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File(logTemplate, rollingInterval: RollingInterval.Day)
                .CreateLogger();

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
                    Kiriha.Core.CrashReporter.WriteCrash(ex, "AppDomain.UnhandledException");
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
                Kiriha.Core.CrashReporter.WriteCrash(ex, "Program.Main");
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
                var end = a.IndexOfAny(new[] { '&', ' ' }, idx);
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
