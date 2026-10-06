using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lexis.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDocumentChunkStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PageNumber",
                table: "DocumentChunks");

            migrationBuilder.AddColumn<int>(
                name: "ChunkType",
                table: "DocumentChunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.AddColumn<int>(
                name: "EndPage",
                table: "DocumentChunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Heading",
                table: "DocumentChunks",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartPage",
                table: "DocumentChunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChunkType",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "ContinuesNext",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "ContinuesPrevious",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EndPage",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "Heading",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "StartPage",
                table: "DocumentChunks");

            migrationBuilder.AddColumn<int>(
                name: "PageNumber",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);
        }
    }
}
