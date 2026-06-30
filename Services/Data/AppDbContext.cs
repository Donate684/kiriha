using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Services.Data;

public class AppDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AnimeItem> UserAnime { get; set; } = null!;
    public DbSet<HistoryItem> History { get; set; } = null!;
    public DbSet<ShikiMetadata> Metadata { get; set; } = null!;
    public DbSet<SyncTaskEntity> SyncTasks { get; set; } = null!;
    public DbSet<FileRecognitionCache> FileRecognitionCache { get; set; } = null!;
    public DbSet<EpisodeRelease> EpisodeReleases { get; set; } = null!;
    public DbSet<MalSearchCache> MalSearchCache { get; set; } = null!;
    public DbSet<HttpCacheEntry> HttpResponseCache { get; set; } = null!;
    public DbSet<EpisodeListMeta> EpisodeListMeta { get; set; } = null!;
    public DbSet<AnimeRelation> AnimeRelations { get; set; } = null!;
    public DbSet<AnimeRelationMeta> AnimeRelationMeta { get; set; } = null!;
    public DbSet<AnimeStaff> AnimeStaff { get; set; } = null!;
    public DbSet<AnimeStaffMeta> AnimeStaffMeta { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 1. Automated Naming (PascalCase -> snake_case)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName != null) entity.SetTableName(ToSnakeCase(tableName));

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }

        // 2. Specific Table Configurations
        modelBuilder.Entity<AnimeItem>(entity =>
        {
            entity.ToTable("user_anime");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            
            entity.Property(e => e.MediaKind)
                  .HasConversion<string>()
                  .HasDefaultValue(MediaKind.Anime);

            entity.Property(e => e.Status)
                  .HasConversion(
                      v => StatusMapper.ToDbString(v),
                      v => StatusMapper.FromDbString(v)
                  );

            ConfigureJsonList(entity, e => e.Genres);
            ConfigureJsonList(entity, e => e.Studios);
            ConfigureJsonList(entity, e => e.AlternativeTitles);

            // Ignored UI/Calculated properties
            entity.Ignore(e => e.DisplayTitle);
            entity.Ignore(e => e.DisplaySynopsis);
            entity.Ignore(e => e.ProgressValue);
            entity.Ignore(e => e.ProgressDisplay);
            entity.Ignore(e => e.Season);
            entity.Ignore(e => e.AiringBadgeText);
            
            entity.HasIndex(e => e.RussianTitle).HasDatabaseName("idx_user_anime_russian_title");
        });

        modelBuilder.Entity<HistoryItem>(entity =>
        {
            entity.ToTable("history");
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<FileRecognitionCache>(entity =>
        {
            entity.HasKey(e => e.FileHash);
        });

        modelBuilder.Entity<MalSearchCache>(entity =>
        {
            entity.ToTable("mal_search_cache");
            entity.HasKey(e => e.QueryNormalized);
        });

        modelBuilder.Entity<HttpCacheEntry>(entity =>
        {
            entity.ToTable("http_response_cache");
            entity.HasKey(e => e.UrlHash);
        });

        modelBuilder.Entity<EpisodeListMeta>(entity =>
        {
            entity.ToTable("episode_list_meta");
            entity.HasKey(e => e.MalId);
            entity.Property(e => e.MalId).ValueGeneratedNever();
        });

        modelBuilder.Entity<EpisodeRelease>(entity =>
        {
            entity.ToTable("episode_releases");
            // Lookups in JikanApiService.GetEpisodesByMalIdAsync filter by mal_id;
            // without this index the cache check degrades to a table scan once a
            // user accumulates a few hundred series in their list.
            entity.HasIndex(e => e.MalId).HasDatabaseName("idx_episode_releases_mal_id");
        });

        modelBuilder.Entity<SyncTaskEntity>(entity =>
        {
            entity.ToTable("sync_tasks");
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<AnimeRelation>(entity =>
        {
            entity.ToTable("anime_relations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.SourceMalId).HasDatabaseName("idx_anime_relations_source_mal_id");
        });

        modelBuilder.Entity<AnimeRelationMeta>(entity =>
        {
            entity.ToTable("anime_relation_meta");
            entity.HasKey(e => e.MalId);
            entity.Property(e => e.MalId).ValueGeneratedNever();
        });

        modelBuilder.Entity<AnimeStaff>(entity =>
        {
            entity.ToTable("anime_staff");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.SourceMalId).HasDatabaseName("idx_anime_staff_source_mal_id");
        });

        modelBuilder.Entity<AnimeStaffMeta>(entity =>
        {
            entity.ToTable("anime_staff_meta");
            entity.HasKey(e => e.MalId);
            entity.Property(e => e.MalId).ValueGeneratedNever();
        });
    }

    private static void ConfigureJsonList<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity, System.Linq.Expressions.Expression<System.Func<TEntity, List<string>?>> propertyExpression) where TEntity : class
    {
        entity.Property(propertyExpression)
              .HasConversion(
                  v => JsonSerializer.Serialize(v, JsonOptions),
                  v => JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>()
              );
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, @"([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
    }
}
