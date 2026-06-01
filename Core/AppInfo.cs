using System;
using System.Reflection;

namespace Kiriha.Core;

/// <summary>
/// Single source of truth for the runtime application identity (assembly version,
/// User-Agent string). Keeps every outbound HTTP client honest about which build
/// is talking to MAL/Shiki/Jikan, instead of three hard-coded "Kiriha/1.x" values
/// drifting independently across the codebase.
///
/// Resolution order for <see cref="Version"/>:
///   1. <see cref="AssemblyInformationalVersionAttribute"/> (set by csproj &lt;Version&gt;)
///   2. <see cref="AssemblyName.Version"/>
///   3. literal "0.0.0"
/// </summary>
public static class AppInfo
{
    /// <summary>Semantic version of the running build (e.g. "0.2.0").</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// HTTP User-Agent for every Kiriha-originated request.
    /// Format: "Kiriha/&lt;version&gt;" — short, parseable, identifiable in upstream logs.
    /// </summary>
    public static string UserAgent { get; } = $"{Constants.System.AppName}/{Version}";

    private static string ResolveVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                // InformationalVersion sometimes carries a "+commit" build metadata suffix —
                // strip it so the UA looks like "Kiriha/0.2.0", not "Kiriha/0.2.0+abc123".
                var plus = info.IndexOf('+');
                return plus >= 0 ? info.Substring(0, plus) : info;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }
}
