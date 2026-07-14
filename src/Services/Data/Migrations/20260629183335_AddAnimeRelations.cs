using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnimeRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anime_relation_meta",
                columns: table => new
                {
                    mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anime_relation_meta", x => x.mal_id);
                });

            migrationBuilder.CreateTable(
                name: "anime_relations",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source_mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    relation_type = table.Column<string>(type: "TEXT", nullable: false),
                    target_mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    target_type = table.Column<string>(type: "TEXT", nullable: false),
                    target_name = table.Column<string>(type: "TEXT", nullable: false),
                    target_url = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anime_relations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_anime_relations_source_mal_id",
                table: "anime_relations",
                column: "source_mal_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anime_relation_meta");

            migrationBuilder.DropTable(
                name: "anime_relations");
        }
    }
}
