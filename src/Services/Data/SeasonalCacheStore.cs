using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Platform;
using Kiriha.Models;
using Serilog;

namespace Kiriha.Services.Data;

/// <summary>
/// Disk persistence for the seasonal anime cache.
///
/// Each (year, season) lives in its own JSON file under
/// <c>{BasePath}/seasonal_cache/{year}_{season}.json</c>. Atomic writes
/// (temp + File.Move) avoid torn files on crash. Files older than
/// <see cref="Ttl"/> are treated as missing and deleted on load.
///
/// Rationale: <see cref="Kiriha.ViewModels.SeasonalViewModel"/> already
/// implements stale-while-revalidate over an in-memory cache. Persisting that
/// cache across restarts gives a near-instant first paint of the seasonal
/// view — fresh data still flows in the background a beat later.
/// </summary>
public sealed class SeasonalCacheStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly string _root;
    // Per-key write serialization. Avoids overlapping writes for the same
    // season tearing each other (e.g. background refresh writing while the
    // user-triggered fetch also writes). File.Move is atomic, but two
    // concurrent renames into the same destination is a coin flip on NTFS.
    private readonly Dictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly object _writeLocksGate = new();

    public SeasonalCacheStore()
    {
        _root = PathHelper.GetSeasonalCachePath();
        try { Directory.CreateDirectory(_root); }
        catch (Exception ex) { Log.Warning(ex, "SeasonalCacheStore: failed to create cache directory"); }
    }

    /// <summary>
    /// Eagerly reads every non-expired cache file from disk. Called once at
    /// SeasonalViewModel construction; expected to complete in well under
    /// 100 ms for a few-dozen-file directory.
    /// </summary>
    public IReadOnlyList<(int Year, string Season, List<AnimeItem> Items)> LoadAll()
    {
        var results = new List<(int, string, List<AnimeItem>)>();
        if (!Directory.Exists(_root)) return results;

        var threshold = DateTime.UtcNow - Ttl;

        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < threshold)
                {
                    // Expired — best-effort delete so the directory doesn't
                    // accumulate decades of stale seasons.
                    try { info.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete expired seasonal cache file {File}", file); }
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(file);
                if (!TryParseKey(name, out int year, out string season)) continue;

                var json = File.ReadAllText(file);
                var items = JsonSerializer.Deserialize<List<AnimeItem>>(json, JsonOptions);
                if (items == null || items.Count == 0) continue;

                results.Add((year, season, items));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SeasonalCacheStore: failed to load {File}", file);
                // Corrupt file — drop it so we don't keep tripping on it.
                try { File.Delete(file); } catch (Exception delEx) { Log.Debug(delEx, "Failed to delete corrupt seasonal cache file {File}", file); }
            }
        }

        return results;
    }

    public async Task SaveAsync(int year, string season, IReadOnlyList<AnimeItem> items)
    {
        if (items == null || items.Count == 0) return;
        if (string.IsNullOrEmpty(season)) return;

        string key = MakeKey(year, season);
        string finalPath = Path.Combine(_root, key + ".json");
        string tmpPath = finalPath + ".tmp";

        var gate = GetWriteLock(key);
        await gate.WaitAsync();
        try
        {
            // Run JSON serialization + I/O off the calling thread (likely UI).
            await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(_root);
                    var json = JsonSerializer.Serialize(items, JsonOptions);
                    File.WriteAllText(tmpPath, json);
                    File.Move(tmpPath, finalPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "SeasonalCacheStore: failed to save {Key}", key);
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch (Exception delEx) { Log.Debug(delEx, "Failed to delete temp seasonal cache file {TmpPath}", tmpPath); }
                }
            });
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GetWriteLock(string key)
    {
        lock (_writeLocksGate)
        {
            if (!_writeLocks.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _writeLocks[key] = gate;
            }
            return gate;
        }
    }

    private static string MakeKey(int year, string season) =>
        $"{year}_{season.ToLowerInvariant()}";

    private static bool TryParseKey(string name, out int year, out string season)
    {
        year = 0; season = string.Empty;
        var idx = name.IndexOf('_');
        if (idx <= 0 || idx == name.Length - 1) return false;
        if (!int.TryParse(name.AsSpan(0, idx), out year)) return false;
        season = name.Substring(idx + 1);
        // Capitalize first letter to match Constants.Seasons.* casing.
        if (season.Length > 0)
            season = char.ToUpperInvariant(season[0]) + season.Substring(1);
        return true;
    }

}
