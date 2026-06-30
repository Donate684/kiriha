using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kiriha.Services.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMangaSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "chapters",
                table: "user_anime",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "chapters_read",
                table: "user_anime",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "media_kind",
                table: "user_anime",
                type: "TEXT",
                nullable: false,
                defaultValue: "Anime");

            migrationBuilder.AddColumn<int>(
                name: "volumes",
                table: "user_anime",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "volumes_read",
                table: "user_anime",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chapters",
                table: "user_anime");

            migrationBuilder.DropColumn(
                name: "chapters_read",
                table: "user_anime");

            migrationBuilder.DropColumn(
                name: "media_kind",
                table: "user_anime");

            migrationBuilder.DropColumn(
                name: "volumes",
                table: "user_anime");

            migrationBuilder.DropColumn(
                name: "volumes_read",
                table: "user_anime");
        }
    }
}
