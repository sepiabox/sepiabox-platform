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
