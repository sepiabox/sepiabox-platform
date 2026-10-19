using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    location = table.Column<Point>(type: "geography (Point, 4326)", nullable: true),
                    camera_make = table.Column<string>(type: "text", nullable: true),
                    camera_model = table.Column<string>(type: "text", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "manifest_imports",
                columns: table => new
                {
                    blob_name = table.Column<string>(type: "text", nullable: false),
                    etag = table.Column<string>(type: "text", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manifest_imports", x => x.blob_name);
                });

            migrationBuilder.CreateTable(
                name: "asset_sources",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_id = table.Column<string>(type: "character varying(64)", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_sources", x => x.id);
                    table.ForeignKey(
                        name: "fk_asset_sources_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "derivatives",
                columns: table => new
                {
                    asset_id = table.Column<string>(type: "character varying(64)", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    blob_path = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_derivatives", x => new { x.asset_id, x.name });
                    table.ForeignKey(
                        name: "fk_derivatives_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asset_sources_asset_id_type_name_path",
                table: "asset_sources",
                columns: new[] { "asset_id", "type", "name", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_captured_at",
                table: "assets",
                column: "captured_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_sources");

            migrationBuilder.DropTable(
                name: "derivatives");

            migrationBuilder.DropTable(
                name: "manifest_imports");

            migrationBuilder.DropTable(
                name: "assets");
        }
    }
}
