using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sepiabox.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class QuestionUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "question_usage",
                columns: table => new
                {
                    user_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    questions = table.Column<int>(type: "integer", nullable: false),
                    input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_read_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    cache_creation_input_tokens = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_usage", x => new { x.user_key, x.day });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "question_usage");
        }
    }
}
