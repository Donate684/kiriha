using System;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Composition;

/// <summary>
/// DI registrations for the data access layer:
///   * EF Core context factory (Sqlite, NoTracking, shared cache)
///   * Settings service
///   * SQLite-backed per-aggregate repositories (UserAnime, Metadata,
///     MalSearchCache, HttpCache, EpisodeReleases, History, SyncTasks)
///   * In-memory caches and per-feature data services
///   * Authentication services for upstream APIs
///   * HTTP client wiring for MAL/Shiki/RSS pipelines
///
/// Kept as an extension method instead of a free-form block in
/// <c>App.ConfigureServices</c> so the lifetime decisions for the data layer
/// live next to each other and a future PR adding a new repo touches one file
/// instead of editing the App constructor.
/// </summary>
internal static class DataServicesRegistration
{
    public static IServiceCollection AddKirihaData(this IServiceCollection services, string dbPath)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath};Cache=Shared")
                   .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
#if DEBUG
            // Sensitive data logging leaks parameter values (incl. tokens stored on entities)
            // into the EF logger. Keep it strictly out of release builds.
            options.EnableSensitiveDataLogging();
#endif
        });

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AppReadinessService>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<DatabaseMaintenance>();
        services.AddSingleton<CacheCleanupService>();

        // Per-aggregate repositories. Replaces the monolithic DatabaseService —
        // every consumer now depends on the narrowest interface that covers its
        // queries, so a future swap of the storage layer can happen one
        // aggregate at a time.
        services.AddSingleton<IUserAnimeRepository, UserAnimeRepository>();
        services.AddSingleton<IMetadataRepository, MetadataRepository>();
        services.AddSingleton<IMalSearchCacheRepository, MalSearchCacheRepository>();
        services.AddSingleton<IHttpCacheRepository, HttpCacheRepository>();
        services.AddSingleton<IEpisodeReleaseRepository, EpisodeReleaseRepository>();
        services.AddSingleton<IAnimeRelationRepository, AnimeRelationRepository>();
        services.AddSingleton<IAnimeStaffRepository, AnimeStaffRepository>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<ISyncTaskRepository, SyncTaskRepository>();
        services.AddSingleton<LocalizationService>();
        services.AddSingleton<ShikiMetadataService>();
        services.AddHttpClient("ImageClient", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent);
        });
        services.AddSingleton<ImageCacheService>();
        services.AddSingleton<SeasonalCacheStore>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<ManualMappingService>();
        services.AddSingleton<RecognitionCache>();
        services.AddSingleton<MappingService>();
        services.AddSingleton<SystemIntegrationService>();
        services.AddSingleton<IPlayerMediaMetadataResolver, PlayerMediaMetadataResolver>();
        services.AddSingleton<FaviconService>();

        return services;
    }
}
