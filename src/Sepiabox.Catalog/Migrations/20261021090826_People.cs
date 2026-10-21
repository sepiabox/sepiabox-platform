using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class People : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "faces_version",
                table: "assets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    user_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    face_count = table.Column<int>(type: "integer", nullable: false),
                    centroid = table.Column<Vector>(type: "vector(128)", nullable: true),
                    faces_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faces",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    asset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    box = table.Column<double[]>(type: "double precision[]", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    embedding = table.Column<Vector>(type: "vector(128)", nullable: false),
                    person_id = table.Column<long>(type: "bigint", nullable: true),
                    assignment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faces", x => x.id);
                    table.CheckConstraint("ck_faces_assignment", "assignment IN ('auto', 'manual', 'excluded')");
                    table.ForeignKey(
                        name: "fk_faces_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_faces_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_faces_asset_id",
                table: "faces",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_faces_person_id",
                table: "faces",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_centroid",
                table: "people",
                column: "centroid")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_people_face_count",
                table: "people",
                columns: new[] { "face_count", "id" },
                descending: new[] { true, false });

            // Manifests imported before this migration may already carry faces (S-018), which the import skipped
            // and an unchanged blob would never bring back: forget which versions were imported, so the next
            // import (sepiabox-import-all after the deploy) reads every manifest once more. Importing is
            // idempotent (D-032), so nothing else changes (D-077).
            migrationBuilder.Sql("DELETE FROM manifest_imports;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faces");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropColumn(
                name: "faces_version",
                table: "assets");
        }
    }
}
