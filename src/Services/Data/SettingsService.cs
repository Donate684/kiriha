using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.Services.Data;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}

[Flags]
public enum SettingsSection
{
    None = 0,
    UI = 1 << 0,
    System = 1 << 1,
    Player = 1 << 2,
    Torrents = 1 << 3,
    Api = 1 << 4,
    CustomLinks = 1 << 5,
    All = UI | System | Player | Torrents | Api | CustomLinks
}

public partial class SettingsService : IDisposable
{
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

    private AppSettings _current = new();
    public AppSettings Current => Volatile.Read(ref _current);

    public SettingsService(string? settingsPath = null)
    {
        var sw = Stopwatch.StartNew();
        _settingsPath = settingsPath ?? Kiriha.Core.Platform.PathHelper.GetSettingsPath();
        _debouncer = new Debouncer(TimeSpan.FromMilliseconds(500), async (_) => await SaveAsync());
        Load();
        Log.Information("StartupTiming: settings service initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
    }

    public void Load()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!File.Exists(_settingsPath))
            {
                Log.Information("Settings file not found, creating new one");
                SaveImmediate();
                return;
            }

            var loaded = LoadSettingsFile(_settingsPath)
                ?? throw new JsonException("Settings file contained null JSON");
            SetCurrent(loaded);

            Log.Information("Settings loaded from {Path} elapsedMs={ElapsedMs}", _settingsPath, sw.ElapsedMilliseconds);
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Error loading settings; file is temporarily unavailable, fallback will not be saved automatically");
            SetCurrent(new AppSettings());
            Log.Information("Settings fallback initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            var backup = TryLoadBackupSettings(ex);
            if (backup != null)
            {
                SetCurrent(backup);
                MarkAllSectionsChanged();
                Log.Information("Settings restored from backup elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
                return;
            }

            Log.Error(ex, "Error loading settings");
            SetCurrent(new AppSettings());
            MarkAllSectionsChanged();
            Log.Information("Settings fallback initialized elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        }
    }

    public void Save() => _debouncer.Invoke();

    public void Update(Action<AppSettings> update, bool save = true)
    {
        if (update == null) throw new ArgumentNullException(nameof(update));

        lock (_stateLock)
        {
            var clone = CloneSettings(_current);
            update(clone);
            Volatile.Write(ref _current, clone);
            MarkAllSectionsChanged();
        }

        if (save) Save();
    }

    public void Update(Action<AppSettings> update, SettingsSection changedSections, bool save = true)
    {
        if (update == null) throw new ArgumentNullException(nameof(update));

        lock (_stateLock)
        {
            var clone = CloneSettings(_current);
            update(clone);
            Volatile.Write(ref _current, clone);
            MarkChangedSections(changedSections);
        }

        if (save) Save();
    }

    public T Read<T>(Func<AppSettings, T> read)
    {
        if (read == null) throw new ArgumentNullException(nameof(read));

        lock (_stateLock)
        {
            return read(_current);
        }
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

    private void MarkChangedSections(SettingsSection sections)
    {
        lock (_stateLock)
        {
            if (sections.HasFlag(SettingsSection.UI)) _uiVersion++;
            if (sections.HasFlag(SettingsSection.System)) _systemVersion++;
            if (sections.HasFlag(SettingsSection.Player)) _playerVersion++;
            if (sections.HasFlag(SettingsSection.Torrents)) _torrentsVersion++;
            if (sections.HasFlag(SettingsSection.Api)) _apiVersion++;
            if (sections.HasFlag(SettingsSection.CustomLinks)) _customLinksVersion++;
        }
    }

    private void MarkAllSectionsChanged()
    {
        lock (_stateLock)
        {
            _uiVersion++;
            _systemVersion++;
            _playerVersion++;
            _torrentsVersion++;
            _apiVersion++;
            _customLinksVersion++;
        }
    }








    private void SetCurrent(AppSettings settings)
    {
        lock (_stateLock)
        {
            Volatile.Write(ref _current, settings);
        }
    }

    public void Dispose()
    {
        try
        {
            _debouncer.CancelPending();
            SaveImmediate();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SettingsService: final save failed during dispose");
        }

        _debouncer.Dispose();
        _saveLock.Dispose();
    }
}
