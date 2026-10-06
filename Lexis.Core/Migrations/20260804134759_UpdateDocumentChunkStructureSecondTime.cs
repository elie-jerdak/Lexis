using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lexis.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDocumentChunkStructureSecondTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContinuesNext",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "ContinuesPrevious",
                table: "DocumentChunks");

            migrationBuilder.AddColumn<int>(
                name: "ContinuationType",
                table: "DocumentChunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FigureId",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeadingPath",
                table: "DocumentChunks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TableId",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContinuationType",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "FigureId",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "HeadingPath",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "TableId",
                table: "DocumentChunks");

            migrationBuilder.AddColumn<bool>(
                name: "ContinuesNext",
                table: "DocumentChunks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ContinuesPrevious",
                table: "DocumentChunks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
