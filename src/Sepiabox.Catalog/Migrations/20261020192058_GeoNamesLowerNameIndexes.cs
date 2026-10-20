using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <summary>
    /// Expression indexes for the search's city matching (S-013, D-069), which compares <c>lower(ascii_name)</c> and
    /// <c>lower(name)</c> with the folded and lower-cased input. EF Core models no expression index, so they are SQL.
    /// </summary>
    public partial class GeoNamesLowerNameIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX ix_geonames_lower_ascii_name ON geonames (lower(ascii_name));");
            migrationBuilder.Sql("CREATE INDEX ix_geonames_lower_name ON geonames (lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_geonames_lower_name;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_geonames_lower_ascii_name;");
        }
    }
}
