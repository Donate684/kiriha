using System;
using System.Diagnostics;
using Serilog;

namespace Kiriha.Core.Platform;

/// <summary>
/// Thin wrapper over <see cref="Process.Start(ProcessStartInfo)"/> for opening
/// URLs and files via the OS shell. Windows-only: the project targets
/// <c>net10.0-windows</c>, so the previous Linux/macOS branches were dead
/// code and have been retired. Add them back behind a runtime check only if
/// the target framework changes.
/// </summary>
public static class ShellLauncher
{
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            // UseShellExecute=true so the system browser handles registration
            // and protocol resolution (about:, file:, custom schemes, etc.).
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ShellLauncher: failed to open URL {Url}", url);
        }
    }
}
