using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Kiriha.Core.Platform;
using Serilog;

namespace Kiriha.Core.Infrastructure;

/// <summary>
/// Persists crash snapshots to disk so the next application launch can surface
/// them to the user. Writes a self-contained text file (no log rotation can
/// remove evidence) under <c>&lt;logs&gt;/crashes/</c>.
/// </summary>
public static class CrashReporter
{
    private const string CrashesSubDir = "crashes";
    private const string SeenSubDir = "seen";
    private const int LogTailLines = 500;

    private static string CrashesDir => Path.Combine(PathHelper.GetLogsPath(), CrashesSubDir);
    private static string SeenDir => Path.Combine(CrashesDir, SeenSubDir);

    public static void WriteCrash(Exception? exception, string source)
    {
        try
        {
            Directory.CreateDirectory(CrashesDir);
            // UTC + invariant culture: monotonic across DST transitions, no locale surprises.
            var fileName = $"crash-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffZ", System.Globalization.CultureInfo.InvariantCulture)}.txt";
            var filePath = Path.Combine(CrashesDir, fileName);
            File.WriteAllText(filePath, BuildReport(exception, source), Encoding.UTF8);
            Log.Information("CrashReporter: Wrote crash snapshot {File}", filePath);
        }
        catch (Exception ex)
        {
            // Crashing while reporting a crash — just log and move on.
            Log.Error(ex, "CrashReporter: Failed to write crash snapshot");
        }
    }

    /// <summary>
    /// Returns the most recent unseen crash file, or null if none.
    /// </summary>
    public static string? GetPendingCrashFile()
    {
        try
        {
            if (!Directory.Exists(CrashesDir)) return null;
            var files = Directory.GetFiles(CrashesDir, "crash-*.txt", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return null;
            // Sort by mtime, not filename: future format changes (locale, prefix) won't break
            // ordering, and clock-skew between runs is bounded by file-system timestamps.
            return files
                .Select(f => new { Path = f, Time = SafeGetWriteTime(f) })
                .OrderByDescending(x => x.Time)
                .First()
                .Path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReporter: GetPendingCrashFile failed");
            return null;
        }
    }

    public static string ReadReport(string crashFilePath)
    {
        try { return File.ReadAllText(crashFilePath, Encoding.UTF8); }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReporter: ReadReport failed for {File}", crashFilePath);
            return $"(failed to read crash file: {ex.Message})";
        }
    }

    /// <summary>
    /// Marks a crash file as seen by moving it to <c>crashes/seen/</c>. Keeps history
    /// for diagnostics without re-prompting the user on next launch.
    /// </summary>
    public static void MarkSeen(string crashFilePath)
    {
        try
        {
            if (!File.Exists(crashFilePath)) return;
            Directory.CreateDirectory(SeenDir);
            var dest = Path.Combine(SeenDir, Path.GetFileName(crashFilePath));
            // If a file with same name already exists in seen, overwrite.
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(crashFilePath, dest);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReporter: MarkSeen failed for {File}", crashFilePath);
        }
    }

    public static string GetCrashesDir()
    {
        Directory.CreateDirectory(CrashesDir);
        return CrashesDir;
    }

    private static string BuildReport(Exception? exception, string source)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Kiriha crash report ===");
        sb.AppendLine($"Time      : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Source    : {source}");
        sb.AppendLine($"Version   : {GetAppVersion()}");
        sb.AppendLine($"OS        : {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
        sb.AppendLine($"Runtime   : {Environment.Version} / {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"CPUs      : {Environment.ProcessorCount}");
        sb.AppendLine($"WorkSet   : {Environment.WorkingSet / (1024 * 1024)} MB");
        sb.AppendLine();

        sb.AppendLine("=== Exception ===");
        if (exception != null)
        {
            // ToString() includes the full chain (inner exceptions + stack traces).
            sb.AppendLine(exception.ToString());
        }
        else
        {
            sb.AppendLine("(no exception object captured)");
        }
        sb.AppendLine();

        sb.AppendLine($"=== Last {LogTailLines} log lines ===");
        sb.AppendLine(ReadLogTail(LogTailLines));

        return sb.ToString();
    }

    private static string GetAppVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info)) return info;
            return asm.GetName().Version?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "CrashReporter: GetAppVersion failed");
            return "unknown";
        }
    }

    private static DateTime SafeGetWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception ex)
        {
            Log.Debug(ex, "CrashReporter: GetLastWriteTimeUtc failed for {File}", path);
            return DateTime.MinValue;
        }
    }

    private static string ReadLogTail(int maxLines)
    {
        try
        {
            var logsDir = PathHelper.GetLogsPath();
            if (!Directory.Exists(logsDir)) return "(no logs directory)";

            var latest = Directory.GetFiles(logsDir, "kiriha-*.txt", SearchOption.TopDirectoryOnly)
                                  .OrderByDescending(f => f)
                                  .FirstOrDefault();
            if (latest == null) return "(no log file found)";

            // Read tail without loading the entire file.
            var queue = new Queue<string>(maxLines);
            using var fs = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (queue.Count == maxLines) queue.Dequeue();
                queue.Enqueue(line);
            }
            return string.Join(Environment.NewLine, queue);
        }
        catch (Exception ex)
        {
            return $"(failed to read log tail: {ex.Message})";
        }
    }
}
