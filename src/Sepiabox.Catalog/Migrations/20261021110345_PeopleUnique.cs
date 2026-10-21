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
            migrationBuilder.CreateIndex(
                name: "ix_people_user_email",
                table: "people",
                column: "user_email",
                unique: true);

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
        }
    }
}
