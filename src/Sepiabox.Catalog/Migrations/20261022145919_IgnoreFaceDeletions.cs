using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class IgnoreFaceDeletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_face_data_deletions_scope",
                table: "face_data_deletions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_face_data_deletions_scope",
                table: "face_data_deletions",
                sql: "scope IN ('library', 'person', 'ignore')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A row of the new scope would break the old constraint: it was a deletion of faces all the same.
            migrationBuilder.Sql("UPDATE face_data_deletions SET scope = 'person' WHERE scope = 'ignore'");

            migrationBuilder.DropCheckConstraint(
                name: "ck_face_data_deletions_scope",
                table: "face_data_deletions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_face_data_deletions_scope",
                table: "face_data_deletions",
                sql: "scope IN ('library', 'person')");
        }
    }
}
