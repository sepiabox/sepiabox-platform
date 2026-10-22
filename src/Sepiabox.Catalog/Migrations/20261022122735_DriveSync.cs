using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class DriveSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sync_changes_token",
                table: "connected_sources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sync_list_token",
                table: "connected_sources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "sync_listing",
                table: "connected_sources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                table: "asset_sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "drive_files",
                columns: table => new
                {
                    connected_source_id = table.Column<long>(type: "bigint", nullable: false),
                    file_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drive_files", x => new { x.connected_source_id, x.file_id });
                    table.ForeignKey(
                        name: "fk_drive_files_connected_sources_connected_source_id",
                        column: x => x.connected_source_id,
                        principalTable: "connected_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_drive_files_path",
                table: "drive_files",
                columns: new[] { "connected_source_id", "path" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drive_files");

            migrationBuilder.DropColumn(
                name: "sync_changes_token",
                table: "connected_sources");

            migrationBuilder.DropColumn(
                name: "sync_list_token",
                table: "connected_sources");

            migrationBuilder.DropColumn(
                name: "sync_listing",
                table: "connected_sources");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "asset_sources");
        }
    }
}
