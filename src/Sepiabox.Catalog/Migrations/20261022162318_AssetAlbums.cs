using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class AssetAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "caption",
                table: "assets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "captured_at_source",
                table: "assets",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "asset_albums",
                columns: table => new
                {
                    asset_id = table.Column<string>(type: "character varying(64)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_albums", x => new { x.asset_id, x.name });
                    table.ForeignKey(
                        name: "fk_asset_albums_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_albums_name",
                table: "asset_albums",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_albums");

            migrationBuilder.DropColumn(
                name: "caption",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "captured_at_source",
                table: "assets");
        }
    }
}
