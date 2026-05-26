using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Kiriha.Services.Tracking;

public static class StartupService
{
    private const string AppName = "Kiriha";
    private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static void EnableStartup(bool launchMinimized)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
        if (key != null)
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            var command = $"\"{exePath}\"";
            if (launchMinimized)
            {
                command += " --minimized";
            }
            key.SetValue(AppName, command);
        }
    }

    public static void DisableStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
        if (key != null)
        {
            key.DeleteValue(AppName, false);
        }
    }
}
