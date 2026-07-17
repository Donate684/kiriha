using System;
using System.IO;
using Microsoft.Win32;
using Serilog;

namespace Kiriha.Services;

public class SystemIntegrationService
{
    private const string KirihaProgIdPrefix = "io.kiriha.";
    private const string KirihaAppName = "Kiriha";
    private const string AppPathsKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\Kiriha.exe";
    private const string ApplicationsKey = @"Software\Classes\Applications\Kiriha.exe";
    private const string CapabilitiesKey = @"Software\Clients\Media\Kiriha\Capabilities";
    private const string RegisteredApplicationsKey = @"Software\RegisteredApplications";

    private readonly string[] _videoExtensions = new[]
    {
        ".yuv", ".y4m", ".m2ts", ".m2t", ".mts", ".mtv", ".ts", ".tsv", ".tsa", ".tts", ".trp",
        ".mpeg", ".mpg", ".mpe", ".mpeg2", ".m1v", ".m2v", ".mp2v", ".mpv", ".mpv2", ".mod", ".tod",
        ".vob", ".vro", ".evob", ".evo", ".mpeg4", ".m4v", ".mp4", ".mp4v", ".mpg4",
        ".h264", ".avc", ".x264", ".264", ".hevc", ".h265", ".x265", ".265",
        ".ogv", ".ogm", ".ogx", ".mkv", ".mk3d", ".webm", ".avi", ".vfw", ".divx", ".3iv", ".xvid", ".nut",
        ".flic", ".fli", ".flc", ".nsv", ".gxf", ".mxf", ".wm", ".wmv", ".asf",
        ".dvr-ms", ".dvr", ".wtv", ".dv", ".hdv", ".flv", ".f4v", ".qt", ".mov", ".hdmov",
        ".rm", ".rmvb", ".3gpp", ".3gp", ".3gp2", ".3g2"
    };

    public bool IsRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppPathsKey);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public void Register()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var exePath = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exePath)) return;
            
            var commandStr = $"\"{exePath}\" --player \"%1\"";

            // App Paths
            RegSetDefault(Registry.CurrentUser, AppPathsKey, exePath);
            RegAdd(Registry.CurrentUser, AppPathsKey, "UseUrl", 1, RegistryValueKind.DWord);

            // Applications
            RegSetDefault(Registry.CurrentUser, ApplicationsKey + @"\shell", "play");
            RegAdd(Registry.CurrentUser, ApplicationsKey + @"\shell\open", "LegacyDisable", string.Empty);
            RegSetDefault(Registry.CurrentUser, ApplicationsKey + @"\shell\open\command", commandStr);
            RegSetDefault(Registry.CurrentUser, ApplicationsKey + @"\shell\play", "&Play");
            RegSetDefault(Registry.CurrentUser, ApplicationsKey + @"\shell\play\command", commandStr);
            RegAdd(Registry.CurrentUser, ApplicationsKey, "FriendlyAppName", KirihaAppName);

            // OpenWithList (video)
            RegSetDefault(Registry.CurrentUser, @"Software\Classes\SystemFileAssociations\video\OpenWithList\Kiriha.exe", string.Empty);

            // Capabilities
            RegAdd(Registry.CurrentUser, CapabilitiesKey, "ApplicationName", KirihaAppName);
            RegAdd(Registry.CurrentUser, CapabilitiesKey, "ApplicationDescription", "Kiriha Anime Player");

            var fileAssocKey = CapabilitiesKey + @"\FileAssociations";
            var supportedTypesKey = ApplicationsKey + @"\SupportedTypes";

            foreach (var ext in _videoExtensions)
            {
                var progId = KirihaProgIdPrefix + ext.TrimStart('.');
                var friendlyName = "Kiriha Video File";

                // ProgId
                var pidKey = @"Software\Classes\" + progId;
                RegSetDefault(Registry.CurrentUser, pidKey, friendlyName);
                RegAdd(Registry.CurrentUser, pidKey, "EditFlags", 65536, RegistryValueKind.DWord);
                RegAdd(Registry.CurrentUser, pidKey, "FriendlyTypeName", friendlyName);
                RegSetDefault(Registry.CurrentUser, pidKey + @"\DefaultIcon", exePath + ",0");

                RegSetDefault(Registry.CurrentUser, pidKey + @"\shell", "play");
                RegAdd(Registry.CurrentUser, pidKey + @"\shell\open", "LegacyDisable", string.Empty);
                RegSetDefault(Registry.CurrentUser, pidKey + @"\shell\open\command", commandStr);
                RegSetDefault(Registry.CurrentUser, pidKey + @"\shell\play", "&Play");
                RegSetDefault(Registry.CurrentUser, pidKey + @"\shell\play\command", commandStr);

                // Extension
                var extKey = @"Software\Classes\" + ext;
                RegAdd(Registry.CurrentUser, extKey + @"\OpenWithProgIds", progId, string.Empty);
                
                RegAdd(Registry.CurrentUser, supportedTypesKey, ext, string.Empty);
                RegAdd(Registry.CurrentUser, fileAssocKey, ext, progId);
            }

            // RegisteredApplications
            RegAdd(Registry.CurrentUser, RegisteredApplicationsKey, KirihaAppName, @"Software\Clients\Media\Kiriha\Capabilities");
            
            Log.Information("System integration registered successfully in HKCU.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register system integration");
        }
    }

    public void Unregister()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            DeleteKeyTree(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\App Paths\Kiriha.exe");
            DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\Applications\Kiriha.exe");
            DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\SystemFileAssociations\video\OpenWithList\Kiriha.exe");
            DeleteKeyTree(Registry.CurrentUser, @"Software\Clients\Media\Kiriha");

            foreach (var ext in _videoExtensions)
            {
                var progId = KirihaProgIdPrefix + ext.TrimStart('.');
                DeleteKeyTree(Registry.CurrentUser, @"Software\Classes\" + progId);
                DeleteValueSafe(Registry.CurrentUser, @"Software\Classes\" + ext + @"\OpenWithProgIds", progId);
            }

            DeleteValueSafe(Registry.CurrentUser, RegisteredApplicationsKey, KirihaAppName);
            
            Log.Information("System integration unregistered successfully from HKCU.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to unregister system integration");
        }
    }

    private static void RegAdd(RegistryKey hive, string keyPath, string valueName, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        using var key = hive.CreateSubKey(keyPath, true);
        if (key != null)
        {
            key.SetValue(valueName, value, kind);
        }
    }

    private static void RegSetDefault(RegistryKey hive, string keyPath, object value)
    {
        using var key = hive.CreateSubKey(keyPath, true);
        if (key != null)
        {
            key.SetValue("", value);
        }
    }

    private static void DeleteKeyTree(RegistryKey hive, string keyPath)
    {
        try
        {
            hive.DeleteSubKeyTree(keyPath, false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to delete registry key tree: {KeyPath}", keyPath);
        }
    }

    private static void DeleteValueSafe(RegistryKey hive, string keyPath, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(keyPath, true);
            key?.DeleteValue(valueName, false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to delete registry value: {KeyPath}\\{ValueName}", keyPath, valueName);
        }
    }
}
