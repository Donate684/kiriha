using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnimeStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anime_staff",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source_mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    person_mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    person_name = table.Column<string>(type: "TEXT", nullable: false),
                    person_url = table.Column<string>(type: "TEXT", nullable: false),
                    person_image_url = table.Column<string>(type: "TEXT", nullable: false),
                    positions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anime_staff", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "anime_staff_meta",
                columns: table => new
                {
                    mal_id = table.Column<int>(type: "INTEGER", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anime_staff_meta", x => x.mal_id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_anime_staff_source_mal_id",
                table: "anime_staff",
                column: "source_mal_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anime_staff");

            migrationBuilder.DropTable(
                name: "anime_staff_meta");
        }
    }
}
