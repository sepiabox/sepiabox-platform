using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class AssetQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "dhash",
                table: "assets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "hidden",
                table: "assets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "sharpness",
                table: "assets",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_hidden",
                table: "assets",
                column: "hidden",
                filter: "hidden");

            migrationBuilder.CreateIndex(
                name: "ix_assets_sharpness",
                table: "assets",
                column: "sharpness",
                filter: "sharpness IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assets_hidden",
                table: "assets");

            migrationBuilder.DropIndex(
                name: "ix_assets_sharpness",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "dhash",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "hidden",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "sharpness",
                table: "assets");
        }
    }
}
