using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Avalonia;

namespace Kiriha.Core;

/// <summary>
/// Pure UI helpers (no DI, no Avalonia window state). Dialog orchestration
/// moved to <see cref="Kiriha.Core.Dialogs.IDialogService"/>; OS-shell
/// navigation moved to <see cref="ShellLauncher"/>. Anything that needed an
/// <c>App.GetService</c> dependency belongs in a service, not here.
/// </summary>
public static class UIUtils
{
    /// <summary>
    /// Looks up a localised string by key from <c>Application.Resources</c>.
    /// Returns the key itself on miss, which surfaces unlocalised entries
    /// during translation work without crashing the UI.
    /// </summary>
    public static string GetLoc(string key)
    {
        return Application.Current?.Resources[$"l.{key}"] as string ?? key;
    }

    /// <summary>
    /// Localises <paramref name="key"/> and applies <see cref="string.Format(string, object?[])"/>
    /// with <paramref name="args"/>. On a malformed format string returns the
    /// raw pattern instead of throwing.
    /// </summary>
    public static string GetLoc(string key, params object?[] args)
    {
        var pattern = GetLoc(key);
        try { return string.Format(pattern, args); }
        catch { return pattern; }
    }

    /// <summary>
    /// Opens <paramref name="url"/> in the user's default browser.
    /// Delegates to <see cref="ShellLauncher"/>; kept here for source
    /// compatibility with the dozens of callers across views/services.
    /// </summary>
    public static void OpenUrl(string url) => ShellLauncher.OpenUrl(url);
}
