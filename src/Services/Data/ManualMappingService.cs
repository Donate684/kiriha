using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using Serilog;

namespace Kiriha.Services.Data;

public class ManualMappingService
{
    private readonly string _mappingFilePath;
    // ConcurrentDictionary so reads from the matching path don't race with writes from
    // UI commands. Snapshotted (ToArray) on serialization to avoid "Collection was modified".
    private readonly ConcurrentDictionary<string, int> _manualMappings = new();
    private readonly Debouncer _saveDebouncer;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ManualMappingService(string? mappingFilePath = null)
    {
        _mappingFilePath = mappingFilePath ?? PathHelper.GetMappingFilePath();
        _saveDebouncer = new Debouncer(TimeSpan.FromMilliseconds(500), async (ct) => {
            if (!ct.IsCancellationRequested) await SaveMappingsAsync();
        });

        LoadMappings();
    }

    /// <summary>
    /// Sentinel value indicating the title was explicitly unlinked by the user
    /// and must not be auto-matched.
    /// </summary>
    public const int NegativeMappingId = -1;

    public bool TryGetMapping(string normalizedTitle, out int malId)
    {
        var key = AnimeStringHelper.Normalize(normalizedTitle);
        if (_manualMappings.TryGetValue(key, out malId) && malId > 0)
            return true;
        malId = 0;
        return false;
    }

    public bool IsNegativelyMapped(string normalizedTitle)
    {
        var key = AnimeStringHelper.Normalize(normalizedTitle);
        return _manualMappings.TryGetValue(key, out int id) && id == NegativeMappingId;
    }

    public void AddMapping(string title, int malId)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return;
        
        _manualMappings[normalized] = malId;
        _saveDebouncer.Invoke();
        Log.Information("ManualMappingService: Added manual mapping: {Title} ({Normalized}) -> {Id}", title, normalized, malId);
    }

    public void AddNegativeMapping(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return;

        _manualMappings[normalized] = NegativeMappingId;
        _saveDebouncer.Invoke();
        Log.Information("ManualMappingService: Added negative mapping for: {Title} ({Normalized})", title, normalized);
    }

    public bool RemoveMapping(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        string normalized = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrEmpty(normalized)) return false;

        if (_manualMappings.TryRemove(normalized, out _))
        {
            _saveDebouncer.Invoke();
            Log.Information("ManualMappingService: Removed manual mapping for: {Title} ({Normalized})", title, normalized);
            return true;
        }
        return false;
    }

    private void LoadMappings()
    {
        if (!File.Exists(_mappingFilePath)) return;
        try
        {
            var json = File.ReadAllText(_mappingFilePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (loaded == null) return;
            foreach (var kv in loaded) _manualMappings[kv.Key] = kv.Value;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ManualMappingService: Failed to load title mappings");
        }
    }

    private async Task SaveMappingsAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_mappingFilePath);
            if (directory != null && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            // Snapshot the dictionary into a regular Dictionary before serialization. Serializing
            // a ConcurrentDictionary directly is safe but iterates lock-free; taking a snapshot
            // first guarantees a consistent view and is cheap for our small cardinality.
            var snapshot = _manualMappings.ToArray().ToDictionary(kv => kv.Key, kv => kv.Value);
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);

            // Atomic write: write to temp + rename so a process kill mid-write
            // never produces a corrupt mappings file (which would be silently
            // discarded on next launch, losing all manual mappings).
            var tmp = _mappingFilePath + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(_mappingFilePath)) File.Replace(tmp, _mappingFilePath, null);
            else File.Move(tmp, _mappingFilePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ManualMappingService: Failed to save title mappings");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task FlushAsync()
    {
        _saveDebouncer.CancelPending();
        await SaveMappingsAsync();
    }
}
