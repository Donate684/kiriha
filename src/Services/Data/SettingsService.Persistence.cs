using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Kiriha.Models;
using Serilog;

namespace Kiriha.Services.Data;

public partial class SettingsService
{
    public void SaveImmediate()
    {
        bool lockTaken = false;
        try
        {
            lockTaken = _saveLock.Wait(TimeSpan.FromSeconds(2));
            if (lockTaken)
            {
                InternalSaveSync();
            }
            else
            {
                Log.Warning("SettingsService: SaveImmediate timed out waiting for save lock, skipping save to avoid deadlock.");
            }
        }
        catch (ObjectDisposedException)
        {
            // Ignore if disposed
        }
        finally
        {
            if (lockTaken)
            {
                try { _saveLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    public async Task SaveAsync()
    {
        bool lockTaken = false;
        try
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            lockTaken = true;
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (CanSkipSave())
                return;

            await Task.Run(() =>
            {
                EnsureDirectory();
                var save = PrepareJsonForSave();
                var json = EncryptForSave(save.Settings);
                AtomicWrite(_settingsPath, json);
                MarkVersionsSaved(save.Versions);
                Log.Debug("Settings saved (async) to {Path}", _settingsPath);
            }).ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                try { _saveLock.Release(); } catch (ObjectDisposedException) { }
            }
        }
    }

    private void InternalSaveSync()
    {
        if (CanSkipSave())
            return;

        EnsureDirectory();
        var save = PrepareJsonForSave();
        var json = EncryptForSave(save.Settings);
        AtomicWrite(_settingsPath, json);
        MarkVersionsSaved(save.Versions);
        Log.Information("Settings saved to {Path}", _settingsPath);
    }

    /// <summary>
    /// Writes settings to a temp sibling file, then atomically replaces the destination.
    /// Prevents corrupted/half-written settings (and therefore token loss) if the process
    /// is killed mid-write or the disk fills up.
    /// </summary>
    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        // File.Replace requires the destination to exist; fall back to Move on first save.
        if (File.Exists(path)) File.Replace(tmp, path, CanBackupCurrentSettings(path) ? GetBackupPath(path) : null);
        else File.Move(tmp, path);
    }

    private PendingSettingsSave PrepareJsonForSave()
    {
        AppSettings snapshot;
        SettingsVersions versions;
        lock (_stateLock)
        {
            snapshot = CloneSettings(_current);
            versions = GetVersions();
        }

        var merged = TryLoadSettingsFromDisk() ?? snapshot;

        if (versions.Ui != 0) merged.UI = snapshot.UI;
        if (versions.System != 0) merged.System = snapshot.System;
        if (versions.Player != 0) merged.Player = snapshot.Player;
        if (versions.Torrents != 0) merged.Torrents = snapshot.Torrents;
        if (versions.Api != 0) merged.Api = snapshot.Api;
        if (versions.CustomLinks != 0) merged.CustomLinks = snapshot.CustomLinks;

        return new PendingSettingsSave(merged, versions);
    }

    private string EncryptForSave(AppSettings settings)
    {
        var clone = CloneSettings(settings);
        EncryptTokens(clone.Api.Mal);
        EncryptTokens(clone.Api.Shiki);
        return JsonSerializer.Serialize(clone, AppSettingsJsonContext.Default.AppSettings);
    }

    private AppSettings? TryLoadSettingsFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;

            return LoadSettingsFile(_settingsPath) ?? TryLoadBackupSettings();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Settings merge: failed to read current settings from disk");
            return TryLoadBackupSettings();
        }
    }

    private AppSettings? TryLoadBackupSettings(Exception? primaryException = null)
    {
        var backupPath = GetBackupPath(_settingsPath);
        if (!File.Exists(backupPath))
            return null;

        try
        {
            var backup = LoadSettingsFile(backupPath);
            if (backup == null)
                return null;

            if (primaryException != null)
                Log.Warning(primaryException, "Settings load failed; restored from backup {BackupPath}", backupPath);
            else
                Log.Warning("Settings merge: using backup settings from {BackupPath}", backupPath);

            return backup;
        }
        catch (Exception backupException)
        {
            if (primaryException != null)
                Log.Error(backupException, "Error loading settings backup after primary load failed");
            else
                Log.Warning(backupException, "Settings merge: failed to read backup settings");

            return null;
        }
    }

    private AppSettings? LoadSettingsFile(string path)
    {
        var json = ReadAllTextShared(path);
        var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        if (loaded == null)
            return null;

        DecryptTokens(loaded.Api.Mal, loaded.Api);
        DecryptTokens(loaded.Api.Shiki, loaded.Api);
        return loaded;
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, AppSettingsJsonContext.Default.AppSettings);
        return JsonSerializer.Deserialize(bytes, AppSettingsJsonContext.Default.AppSettings)!;
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string GetBackupPath(string path) => path + ".bak";

    private static bool CanBackupCurrentSettings(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                return false;

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            int ch;
            while ((ch = reader.Read()) != -1)
            {
                if (!char.IsWhiteSpace((char)ch))
                    return ch == '{';
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool CanSkipSave()
    {
        if (!File.Exists(_settingsPath))
            return false;

        lock (_stateLock)
        {
            return _uiVersion == 0
                && _systemVersion == 0
                && _playerVersion == 0
                && _torrentsVersion == 0
                && _apiVersion == 0
                && _customLinksVersion == 0;
        }
    }

    private readonly record struct PendingSettingsSave(AppSettings Settings, SettingsVersions Versions);

    private readonly record struct SettingsVersions(
        long Ui,
        long System,
        long Player,
        long Torrents,
        long Api,
        long CustomLinks);

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }
}
