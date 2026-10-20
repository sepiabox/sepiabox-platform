using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class GeoNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "place_city",
                table: "assets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "place_country",
                table: "assets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "place_region",
                table: "assets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "geonames",
                columns: table => new
                {
                    geoname_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ascii_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    country_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    admin1_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    population = table.Column<long>(type: "bigint", nullable: false),
                    location = table.Column<Point>(type: "geography (Point, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geonames", x => x.geoname_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_geonames_location",
                table: "geonames",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "geonames");

            migrationBuilder.DropColumn(
                name: "place_city",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "place_country",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "place_region",
                table: "assets");
        }
    }
}
