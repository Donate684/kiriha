using System;

namespace Kiriha.Core.Platform;

/// <summary>
/// Platform feature probes. Centralised so caller code doesn't sprinkle
/// <c>OperatingSystem.IsWindows()</c> + version checks across the codebase.
/// </summary>
public static class Platform
{
    /// <summary>
    /// Mica window backdrop ships in Windows 11 (build 22000+). On older
    /// Windows or non-Windows the caller should fall back to AcrylicBlur or
    /// a plain solid brush.
    /// </summary>
    public static bool IsMicaSupported =>
        OperatingSystem.IsWindows() && Environment.OSVersion.Version.Build >= 22000;
}
