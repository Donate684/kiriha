using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Serilog;

namespace Kiriha.ViewModels;

public partial class CrashReportViewModel : ObservableObject
{
    [ObservableProperty]
    private string _reportText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    private readonly string? _crashFilePath;

    public CrashReportViewModel() { } // designer

    public CrashReportViewModel(string crashFilePath)
    {
        _crashFilePath = crashFilePath;
        ReportText = CrashReporter.ReadReport(crashFilePath);
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard == null)
            {
                StatusText = UIUtils.GetLoc("crash.status.copy_unavailable");
                return;
            }
            await clipboard.SetTextAsync(ReportText);
            StatusText = UIUtils.GetLoc("crash.status.copied");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReportViewModel: clipboard copy failed");
            StatusText = UIUtils.GetLoc("crash.status.copy_failed");
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var dir = CrashReporter.GetCrashesDir();
            if (!string.IsNullOrEmpty(_crashFilePath) && File.Exists(_crashFilePath))
            {
                // Open folder and select the crash file (Windows Explorer).
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_crashFilePath}\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CrashReportViewModel: open folder failed");
            StatusText = UIUtils.GetLoc("crash.status.open_failed");
        }
    }

    public void MarkSeen()
    {
        if (!string.IsNullOrEmpty(_crashFilePath))
            CrashReporter.MarkSeen(_crashFilePath!);
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;
        return null;
    }
}
