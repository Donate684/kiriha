using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Player;

public static class PlayerProcessBridge
{
    public const string MutexName = "Kiriha.PlayerInstance";
    public const string PipeName = "Kiriha.PlayerInstance";
    public const string ResidentArg = "--player-resident";
    public const string ShutdownArg = "--shutdown-player";
    public const string UpdateMetadataArg = "--player-update-metadata";

    public static bool IsResident(string[] args) =>
        Array.Exists(args, arg => arg.Equals(ResidentArg, StringComparison.OrdinalIgnoreCase));

    public static bool TryForward(string[] args, int timeoutMs = 300)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(PipeArgumentSerializer.Serialize(args));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void StartResident()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return;

        var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
        var isDotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = processPath,
            Arguments = isDotnet && !string.IsNullOrEmpty(assemblyPath)
                ? $"\"{assemblyPath}\" --player {ResidentArg}"
                : $"--player {ResidentArg}",
            UseShellExecute = true,
            WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        try { System.Diagnostics.Process.Start(startInfo); }
        catch (Exception ex) { Log.Debug(ex, "Failed to start resident player process"); }
    }

    public static Task StopResidentAsync()
    {
        return Task.Run(() => TryForward(new[] { "--player", ShutdownArg }, timeoutMs: 500));
    }

    public static void ForwardMetadata(
        string originalTitle,
        int animeId,
        string? titleRu,
        string? titleEn,
        string? episodeText)
    {
        TryForward(new[]
        {
            "--player",
            UpdateMetadataArg,
            "--original-title",
            originalTitle ?? string.Empty,
            "--anime-id",
            animeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--title-ru",
            titleRu ?? string.Empty,
            "--title-en",
            titleEn ?? string.Empty,
            "--episode",
            episodeText ?? string.Empty
        }, timeoutMs: 100);
    }
}
