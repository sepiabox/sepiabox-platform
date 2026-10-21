using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class SavedPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_places",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<Point>(type: "geography (Point, 4326)", nullable: false),
                    radius_m = table.Column<int>(type: "integer", nullable: false),
                    is_home = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_places", x => x.id);
                    table.CheckConstraint("ck_saved_places_radius_m", "radius_m BETWEEN 10 AND 50000");
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_places_home",
                table: "saved_places",
                column: "is_home",
                unique: true,
                filter: "is_home");

            // Names are unique ignoring case (S-014); the model cannot express an index on an expression.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_saved_places_lower_name ON saved_places (lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_places");
        }
    }
}
