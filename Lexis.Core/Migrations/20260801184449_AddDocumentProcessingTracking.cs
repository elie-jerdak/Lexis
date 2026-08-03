using System;
using Lexis.Core.Enums;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lexis.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentProcessingTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingCompletedAt",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingError",
                table: "Documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessingCompletedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ProcessingError",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                table: "Documents");
        }
    }
}
