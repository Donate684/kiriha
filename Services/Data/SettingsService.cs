using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Utils;
using Serilog;

namespace Kiriha.Services.Data;

public class SettingsService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;
    private readonly Debouncer _debouncer;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _stateLock = new();
    private long _uiVersion;
    private long _systemVersion;
    private long _playerVersion;
    private long _torrentsVersion;
    private long _apiVersion;
    private long _customLinksVersion;

    public AppSettings Current { get; private set; } = new();

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Kiriha.Core.PathHelper.GetSettingsPath();
        _debouncer = new Debouncer(TimeSpan.FromMilliseconds(500), async (_) => await SaveAsync());
        Load();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                Log.Information("Settings file not found, creating new one");
                SaveImmediate();
                return;
            }

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            
            if (loaded == null)
            {
                SetCurrent(new AppSettings());
            }
            else
            {
                DecryptTokens(loaded.Api.Mal, loaded.Api);
                DecryptTokens(loaded.Api.Shiki, loaded.Api);
                SetCurrent(loaded);
            }

            Log.Information("Settings loaded from {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading settings");
            SetCurrent(new AppSettings());
        }
    }

    public void Save() => _debouncer.Invoke();

    public void Update(Action<AppSettings> update, bool save = true)
    {
        if (update == null) throw new ArgumentNullException(nameof(update));

        lock (_stateLock)
        {
            var before = CaptureSectionSnapshots(Current);
            update(Current);
            MarkChangedSections(before, CaptureSectionSnapshots(Current));
        }

        if (save) Save();
    }

    public T Read<T>(Func<AppSettings, T> read)
    {
        if (read == null) throw new ArgumentNullException(nameof(read));

        lock (_stateLock)
        {
            return read(Current);
        }
    }

    public void SaveImmediate()
    {
        _saveLock.Wait();
        try
        {
            InternalSaveSync();
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            await InternalSaveAsync();
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void InternalSaveSync()
    {
        EnsureDirectory();
        var save = PrepareJsonForSave();
        var json = EncryptForSave(save.Settings);
        AtomicWrite(_settingsPath, json);
        MarkVersionsSaved(save.Versions);
        Log.Information("Settings saved to {Path}", _settingsPath);
    }

    private async Task InternalSaveAsync()
    {
        EnsureDirectory();
        var save = PrepareJsonForSave();
        var json = EncryptForSave(save.Settings);
        await AtomicWriteAsync(_settingsPath, json);
        MarkVersionsSaved(save.Versions);
        Log.Debug("Settings saved (async) to {Path}", _settingsPath);
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
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    private PendingSettingsSave PrepareJsonForSave()
    {
        AppSettings snapshot;
        SettingsVersions versions;
        lock (_stateLock)
        {
            snapshot = CloneSettings(Current);
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
        return JsonSerializer.Serialize(clone, JsonOptions);
    }

    private AppSettings? TryLoadSettingsFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded == null)
                return null;

            DecryptTokens(loaded.Api.Mal, loaded.Api);
            DecryptTokens(loaded.Api.Shiki, loaded.Api);
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Settings merge: failed to read current settings from disk");
            return null;
        }
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings);
        return JsonSerializer.Deserialize<AppSettings>(json)!;
    }

    private SettingsVersions GetVersions() => new(
        _uiVersion,
        _systemVersion,
        _playerVersion,
        _torrentsVersion,
        _apiVersion,
        _customLinksVersion);

    private void MarkVersionsSaved(SettingsVersions versions)
    {
        lock (_stateLock)
        {
            if (_uiVersion == versions.Ui) _uiVersion = 0;
            if (_systemVersion == versions.System) _systemVersion = 0;
            if (_playerVersion == versions.Player) _playerVersion = 0;
            if (_torrentsVersion == versions.Torrents) _torrentsVersion = 0;
            if (_apiVersion == versions.Api) _apiVersion = 0;
            if (_customLinksVersion == versions.CustomLinks) _customLinksVersion = 0;
        }
    }

    private static SettingsSectionSnapshots CaptureSectionSnapshots(AppSettings settings) => new(
        SerializeSection(settings.UI),
        SerializeSection(settings.System),
        SerializeSection(settings.Player),
        SerializeSection(settings.Torrents),
        SerializeSection(settings.Api),
        SerializeSection(settings.CustomLinks));

    private void MarkChangedSections(SettingsSectionSnapshots before, SettingsSectionSnapshots after)
    {
        if (!string.Equals(before.Ui, after.Ui, StringComparison.Ordinal)) _uiVersion++;
        if (!string.Equals(before.System, after.System, StringComparison.Ordinal)) _systemVersion++;
        if (!string.Equals(before.Player, after.Player, StringComparison.Ordinal)) _playerVersion++;
        if (!string.Equals(before.Torrents, after.Torrents, StringComparison.Ordinal)) _torrentsVersion++;
        if (!string.Equals(before.Api, after.Api, StringComparison.Ordinal)) _apiVersion++;
        if (!string.Equals(before.CustomLinks, after.CustomLinks, StringComparison.Ordinal)) _customLinksVersion++;
    }

    private static string SerializeSection<T>(T value) =>
        JsonSerializer.Serialize(value);

    private readonly record struct PendingSettingsSave(AppSettings Settings, SettingsVersions Versions);

    private readonly record struct SettingsVersions(
        long Ui,
        long System,
        long Player,
        long Torrents,
        long Api,
        long CustomLinks);

    private readonly record struct SettingsSectionSnapshots(
        string Ui,
        string System,
        string Player,
        string Torrents,
        string Api,
        string CustomLinks);

    private void EncryptTokens(object? tokens)
    {
        if (tokens is MalTokens mal)
        {
            mal.AccessToken = Protect(mal.AccessToken);
            mal.RefreshToken = Protect(mal.RefreshToken);
        }
        else if (tokens is ShikiTokens shiki)
        {
            shiki.AccessToken = Protect(shiki.AccessToken);
            shiki.RefreshToken = Protect(shiki.RefreshToken);
        }
    }

    private void DecryptTokens(object? tokens, AppSettings.ApiConfig api)
    {
        if (tokens is MalTokens mal)
        {
            mal.AccessToken = Unprotect(mal.AccessToken);
            mal.RefreshToken = Unprotect(mal.RefreshToken);
            if (string.IsNullOrEmpty(mal.AccessToken)) api.Mal = null;
        }
        else if (tokens is ShikiTokens shiki)
        {
            shiki.AccessToken = Unprotect(shiki.AccessToken);
            shiki.RefreshToken = Unprotect(shiki.RefreshToken);
            if (string.IsNullOrEmpty(shiki.AccessToken)) api.Shiki = null;
        }
    }

    /// <summary>
    /// DPAPI-encrypts an OAuth token for at-rest storage. Returns an empty string
    /// on any failure mode (non-Windows, missing user profile, broken keychain),
    /// which forces the user to re-authenticate rather than silently dumping
    /// plaintext tokens to disk. Better to lose the session than to leak it.
    /// </summary>
    private string Protect(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            Log.Error("SettingsService: token encryption requires Windows DPAPI; refusing to persist plaintext");
            return string.Empty;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes(text);
            var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SettingsService: DPAPI Protect failed; tokens will not be persisted");
            return string.Empty;
        }
    }

    /// <summary>
    /// DPAPI-decrypts an OAuth token. On any failure (different user, corrupted
    /// blob, non-Windows host) returns empty so the consumer treats it as "no
    /// saved token" and triggers a fresh OAuth flow.
    /// </summary>
    private string Unprotect(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            Log.Warning("SettingsService: cannot decrypt tokens off-Windows; user must re-authenticate");
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(encryptedText);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SettingsService: DPAPI Unprotect failed; treating saved token as missing");
            return string.Empty;
        }
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
    }

    private void SetCurrent(AppSettings settings)
    {
        lock (_stateLock)
        {
            Current = settings;
        }
    }

    public void Dispose()
    {
        try
        {
            SaveImmediate();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SettingsService: final save failed during dispose");
        }

        _debouncer.Dispose();
        _saveLock.Dispose();
    }

    public bool NeedsFirstStartup()
    {
        var required = new[] { "language", "theme", "mal_login" };
        return Read(settings => required.Any(step => !settings.System.CompletedSetupSteps.Contains(step)));
    }

    public void CompleteSetupStep(string key)
    {
        var changed = false;
        Update(settings =>
        {
            if (!settings.System.CompletedSetupSteps.Contains(key))
            {
                settings.System.CompletedSetupSteps.Add(key);
                changed = true;
            }
        }, save: false);

        if (changed) Save();
    }
}
