using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lexis.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDocumentStatusEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TYPE document_status ADD VALUE IF NOT EXISTS 'chunked';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            
        }
    }
}
