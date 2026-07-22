using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "episode_list_meta",
                columns: table => new
                {
                    mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_episode_list_meta", x => x.mal_id);
                });

            migrationBuilder.CreateTable(
                name: "episode_releases",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    episode_number = table.Column<int>(type: "INTEGER", nullable: false),
                    air_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                    title = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_episode_releases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_recognition_cache",
                columns: table => new
                {
                    file_hash = table.Column<string>(type: "TEXT", nullable: false),
                    anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                    last_used = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_recognition_cache", x => x.file_hash);
                });

            migrationBuilder.CreateTable(
                name: "history",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                    anime_title = table.Column<string>(type: "TEXT", nullable: false),
                    russian_title = table.Column<string>(type: "TEXT", nullable: true),
                    episode = table.Column<int>(type: "INTEGER", nullable: false),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    action_type = table.Column<int>(type: "INTEGER", nullable: false),
                    detail = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "http_response_cache",
                columns: table => new
                {
                    url_hash = table.Column<string>(type: "TEXT", nullable: false),
                    etag = table.Column<string>(type: "TEXT", nullable: true),
                    last_modified = table.Column<string>(type: "TEXT", nullable: true),
                    body = table.Column<byte[]>(type: "BLOB", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_http_response_cache", x => x.url_hash);
                });

            migrationBuilder.CreateTable(
                name: "mal_search_cache",
                columns: table => new
                {
                    query_normalized = table.Column<string>(type: "TEXT", nullable: false),
                    anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                    score = table.Column<float>(type: "REAL", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mal_search_cache", x => x.query_normalized);
                });

            migrationBuilder.CreateTable(
                name: "metadata",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: true),
                    russian = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    episodes = table.Column<int>(type: "INTEGER", nullable: true),
                    episodes_aired = table.Column<int>(type: "INTEGER", nullable: true),
                    next_episode_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_tasks",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    anime_id = table.Column<int>(type: "INTEGER", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    progress = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: true),
                    score = table.Column<int>(type: "INTEGER", nullable: true),
                    payload = table.Column<string>(type: "TEXT", nullable: true),
                    retry_count = table.Column<int>(type: "INTEGER", nullable: false),
                    successful_trackers_json = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_anime",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    aired_source_priority = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    russian_title = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    progress = table.Column<int>(type: "INTEGER", nullable: false),
                    total_episodes = table.Column<int>(type: "INTEGER", nullable: false),
                    score = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", nullable: false),
                    episodes_aired = table.Column<int>(type: "INTEGER", nullable: false),
                    synopsis = table.Column<string>(type: "TEXT", nullable: true),
                    russian_synopsis = table.Column<string>(type: "TEXT", nullable: true),
                    main_picture_url = table.Column<string>(type: "TEXT", nullable: true),
                    local_poster_path = table.Column<string>(type: "TEXT", nullable: true),
                    nsfw = table.Column<string>(type: "TEXT", nullable: true),
                    english_title = table.Column<string>(type: "TEXT", nullable: true),
                    japanese_title = table.Column<string>(type: "TEXT", nullable: true),
                    alternative_titles = table.Column<string>(type: "TEXT", nullable: false),
                    genres = table.Column<string>(type: "TEXT", nullable: false),
                    studios = table.Column<string>(type: "TEXT", nullable: false),
                    status_detailed = table.Column<string>(type: "TEXT", nullable: true),
                    mean_score = table.Column<string>(type: "TEXT", nullable: true),
                    popularity = table.Column<int>(type: "INTEGER", nullable: false),
                    rank = table.Column<int>(type: "INTEGER", nullable: true),
                    airing_date = table.Column<DateTime>(type: "TEXT", nullable: true),
                    start_season = table.Column<string>(type: "TEXT", nullable: true),
                    start_year = table.Column<int>(type: "INTEGER", nullable: true),
                    rating = table.Column<string>(type: "TEXT", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    is_rewatching = table.Column<bool>(type: "INTEGER", nullable: false),
                    rewatch_count = table.Column<int>(type: "INTEGER", nullable: false),
                    date_started = table.Column<DateTime>(type: "TEXT", nullable: true),
                    date_completed = table.Column<DateTime>(type: "TEXT", nullable: true),
                    broadcast_day = table.Column<string>(type: "TEXT", nullable: true),
                    broadcast_time = table.Column<string>(type: "TEXT", nullable: true),
                    last_episode_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_episodes_sync = table.Column<DateTime>(type: "TEXT", nullable: true),
                    next_episode_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_anime", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_episode_releases_mal_id",
                table: "episode_releases",
                column: "mal_id");

            migrationBuilder.CreateIndex(
                name: "idx_user_anime_russian_title",
                table: "user_anime",
                column: "russian_title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "episode_list_meta");

            migrationBuilder.DropTable(
                name: "episode_releases");

            migrationBuilder.DropTable(
                name: "file_recognition_cache");

            migrationBuilder.DropTable(
                name: "history");

            migrationBuilder.DropTable(
                name: "http_response_cache");

            migrationBuilder.DropTable(
                name: "mal_search_cache");

            migrationBuilder.DropTable(
                name: "metadata");

            migrationBuilder.DropTable(
                name: "sync_tasks");

            migrationBuilder.DropTable(
                name: "user_anime");
        }
    }
}
