using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services.Data;

public enum CacheCleanupTarget
{
    History,
    ImageFiles,
    ApiCache,
    RecognitionCache,
    SeasonalCache
}

public sealed record CacheCleanupStats(CacheCleanupTarget Target, int ItemCount, long SizeBytes);

public sealed class CacheCleanupService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly HistoryService _historyService;

    public CacheCleanupService(IDbContextFactory<AppDbContext> contextFactory, HistoryService historyService)
    {
        _contextFactory = contextFactory;
        _historyService = historyService;
    }

    public async Task<IReadOnlyList<CacheCleanupStats>> GetStatsAsync()
    {
        var stats = new List<CacheCleanupStats>();

        using var context = await _contextFactory.CreateDbContextAsync();

        stats.Add(new CacheCleanupStats(
            CacheCleanupTarget.History,
            await context.History.CountAsync(),
            0));

        stats.Add(GetDirectoryStats(CacheCleanupTarget.ImageFiles, PathHelper.GetImageCachePath()));

        var apiCount =
            await context.Metadata.CountAsync() +
            await context.HttpResponseCache.CountAsync() +
            await context.EpisodeReleases.CountAsync() +
            await context.EpisodeListMeta.CountAsync();
        var httpBytes = await context.HttpResponseCache
            .AsNoTracking()
            .SumAsync(x => (long)x.Body.Length);
        stats.Add(new CacheCleanupStats(
            CacheCleanupTarget.ApiCache,
            apiCount,
            httpBytes));

        stats.Add(new CacheCleanupStats(
            CacheCleanupTarget.RecognitionCache,
            await context.FileRecognitionCache.CountAsync() + await context.MalSearchCache.CountAsync(),
            0));

        stats.Add(GetDirectoryStats(CacheCleanupTarget.SeasonalCache, PathHelper.GetSeasonalCachePath()));

        return stats;
    }

    public async Task ClearAsync(IEnumerable<CacheCleanupTarget> targets)
    {
        var requested = targets.Distinct().ToHashSet();
        if (requested.Count == 0) return;

        await _historyService.FlushAsync(TimeSpan.FromSeconds(5));

        using var context = await _contextFactory.CreateDbContextAsync();

        if (requested.Contains(CacheCleanupTarget.History))
            await context.Database.ExecuteSqlRawAsync("DELETE FROM history;");

        if (requested.Contains(CacheCleanupTarget.ApiCache))
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM metadata;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM http_response_cache;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM episode_releases;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM episode_list_meta;");
        }

        if (requested.Contains(CacheCleanupTarget.RecognitionCache))
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM file_recognition_cache;");
            await context.Database.ExecuteSqlRawAsync("DELETE FROM mal_search_cache;");
        }

        await context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(PASSIVE);");

        if (requested.Contains(CacheCleanupTarget.ImageFiles))
            DeleteDirectoryContents(PathHelper.GetImageCachePath());

        if (requested.Contains(CacheCleanupTarget.SeasonalCache))
            DeleteDirectoryContents(PathHelper.GetSeasonalCachePath());
    }

    private static CacheCleanupStats GetDirectoryStats(CacheCleanupTarget target, string path)
    {
        if (!Directory.Exists(path)) return new CacheCleanupStats(target, 0, 0);

        try
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(x =>
                {
                    try { return new FileInfo(x); }
                    catch (Exception ex) { Log.Debug(ex, "CacheCleanupService: failed to get file info for {File}", x); return null; }
                })
                .Where(x => x is { Exists: true })
                .ToList();

            return new CacheCleanupStats(target, files.Count, files.Sum(x => x!.Length));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CacheCleanupService: failed to inspect {Path}", path);
            return new CacheCleanupStats(target, 0, 0);
        }
    }

    private static void DeleteDirectoryContents(string path)
    {
        if (!Directory.Exists(path)) return;

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList())
        {
            try { File.Delete(file); }
            catch (Exception ex) { Log.Debug(ex, "CacheCleanupService: failed to delete {File}", file); }
        }

        foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                     .OrderByDescending(x => x.Length)
                     .ToList())
        {
            try { Directory.Delete(dir, recursive: false); }
            catch (Exception ex) { Log.Debug(ex, "CacheCleanupService: failed to delete directory {Dir}", dir); }
        }
    }
}
