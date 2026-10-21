using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class PeopleUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_faces_assignment",
                table: "faces");

            migrationBuilder.CreateIndex(
                name: "ix_people_user_email",
                table: "people",
                column: "user_email",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_faces_assignment",
                table: "faces",
                sql: "assignment IN ('auto', 'manual', 'excluded', 'not_face')");

            // Names are unique ignoring case (S-020); the model cannot express an index on an expression.
            migrationBuilder.Sql("CREATE UNIQUE INDEX ix_people_lower_name ON people (lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_people_lower_name;");

            migrationBuilder.DropIndex(
                name: "ix_people_user_email",
                table: "people");

            migrationBuilder.DropCheckConstraint(
                name: "ck_faces_assignment",
                table: "faces");

            // The older schema has no "not a face": those faces stay out of every count as excluded ones.
            migrationBuilder.Sql("UPDATE faces SET assignment = 'excluded' WHERE assignment = 'not_face';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_faces_assignment",
                table: "faces",
                sql: "assignment IN ('auto', 'manual', 'excluded')");
        }
    }
}
