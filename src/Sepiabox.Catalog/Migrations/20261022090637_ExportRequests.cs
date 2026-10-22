using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class ExportRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "export_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    include_face_data = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    blob_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    assets = table.Column<int>(type: "integer", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_requests", x => x.id);
                    table.CheckConstraint("ck_export_requests_status", "status IN ('requested', 'running', 'done', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_export_requests_status",
                table: "export_requests",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_requests");
        }
    }
}
