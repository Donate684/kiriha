using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Kiriha.ViewModels;

/// <summary>
/// Per-anime instance of a <see cref="Kiriha.Models.CustomShareLink"/> with
/// the placeholders already resolved into a concrete URL. Carries its own
/// open command so the share menu / icon row can bind directly to the
/// item without round-tripping through the parent ViewModel via
/// RelativeSource (Avalonia's MenuFlyout makes that awkward).
/// </summary>
public partial class CustomShareLinkRuntime : ObservableObject
{
    public string Name { get; }
    public string IconKind { get; }
    public string Url { get; }

    /// <summary>Absolute path to a user-supplied icon file, or null.</summary>
    public string? IconPath { get; }

    /// <summary>True when <see cref="IconPath"/> points to an existing file.</summary>
    public bool HasIconImage { get; }

    /// <summary>Inverse of <see cref="HasIconImage"/>; bound by the MaterialIcon fallback.</summary>
    public bool UseIconKind => !HasIconImage;

    public CustomShareLinkRuntime(string name, string iconKind, string url, string? iconPath = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? url : name;
        IconKind = string.IsNullOrWhiteSpace(iconKind) ? "Web" : iconKind;
        Url = url;
        IconPath = iconPath;
        HasIconImage = !string.IsNullOrWhiteSpace(iconPath) && System.IO.File.Exists(iconPath);
    }

    [RelayCommand]
    private void Open()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open custom share link {Url}", Url);
        }
    }

    [RelayCommand]
    private async Task Copy()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow?.Clipboard != null)
            {
                await desktop.MainWindow.Clipboard.SetTextAsync(Url);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to copy custom share link {Url}", Url);
        }
    }
}
