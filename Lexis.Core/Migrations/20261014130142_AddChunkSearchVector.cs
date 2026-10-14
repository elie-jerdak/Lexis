using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lexis.API.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkSearchVector : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
        ALTER TABLE "DocumentChunks" ADD COLUMN "SearchVector" tsvector
        GENERATED ALWAYS AS (
            setweight(to_tsvector('simple', coalesce("Heading", '')),     'A') ||
            setweight(to_tsvector('simple', coalesce("Content", '')),     'B') ||
            setweight(to_tsvector('simple', coalesce("HeadingPath", '')), 'C')
        ) STORED;
        """);

            migrationBuilder.Sql("""
        CREATE INDEX "IX_DocumentChunks_SearchVector"
        ON "DocumentChunks" USING GIN ("SearchVector");
        """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_DocumentChunks_SearchVector"";");
            migrationBuilder.Sql(@"ALTER TABLE ""DocumentChunks"" DROP COLUMN IF EXISTS ""SearchVector"";");
        }
    }
}
