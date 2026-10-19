using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class TimelineIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assets_captured_at",
                table: "assets");

            migrationBuilder.CreateIndex(
                name: "ix_assets_timeline",
                table: "assets",
                columns: new[] { "captured_at", "id" },
                descending: new[] { true, false })
                .Annotation("Npgsql:IndexNullSortOrder", new[] { NullSortOrder.NullsLast, NullSortOrder.NullsLast });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_assets_timeline",
                table: "assets");

            migrationBuilder.CreateIndex(
                name: "ix_assets_captured_at",
                table: "assets",
                column: "captured_at");
        }
    }
}
