using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class FaceRecognitionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "erased_faces",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    box = table.Column<double[]>(type: "double precision[]", nullable: false),
                    erased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erased_faces", x => x.id);
                    table.ForeignKey(
                        name: "fk_erased_faces_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "face_data_deletions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    faces = table.Column<int>(type: "integer", nullable: false),
                    people = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_face_data_deletions", x => x.id);
                    table.CheckConstraint("ck_face_data_deletions_scope", "scope IN ('library', 'person')");
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_erased_faces_asset_id",
                table: "erased_faces",
                column: "asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "erased_faces");

            migrationBuilder.DropTable(
                name: "face_data_deletions");

            migrationBuilder.DropTable(
                name: "settings");
        }
    }
}
