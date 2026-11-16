using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shelfy.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Adds a normalized ISBN-13 column (btree) for exact ISBN hits, a visibility index on shelves, and — on PostgreSQL
    /// only — pg_trgm GIN indexes on lower(Title) and lower(Author) so ILIKE/LOWER+LIKE partial matches can use an index.
    /// SQLite and EF in-memory tests never run this raw SQL (they use EnsureCreated).
    /// On PostgreSQL 12, CREATE EXTENSION pg_trgm requires a superuser; on 13+ pg_trgm is trusted and the database owner
    /// can create it. cosmic2 is PostgreSQL 12, so if this step fails, run <c>CREATE EXTENSION IF NOT EXISTS pg_trgm;</c>
    /// as postgres and apply the rest of the migration again.
    /// </summary>
    public partial class AddBookSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Isbn13",
                table: "Books",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            if (IsNpgsql(migrationBuilder))
            {
                // Existing books already store ISBN-13 digits (Isbn.Normalize). Copy those, hyphens stripped, before
                // the unique index is created. ISBN-10 rows are left null here; SaveChanges fills Isbn13 going forward.
                migrationBuilder.Sql(
                    """
                    UPDATE "Books"
                    SET "Isbn13" = regexp_replace("Isbn", '[^0-9]', '', 'g')
                    WHERE "Isbn13" IS NULL
                      AND "Isbn" IS NOT NULL
                      AND regexp_replace("Isbn", '[^0-9]', '', 'g') ~ '^[0-9]{13}$';
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_Shelves_IsPublic_IsHidden",
                table: "Shelves",
                columns: new[] { "IsPublic", "IsHidden" });

            migrationBuilder.CreateIndex(
                name: "IX_Books_Isbn13",
                table: "Books",
                column: "Isbn13",
                unique: true);

            if (IsNpgsql(migrationBuilder))
            {
                migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
                migrationBuilder.Sql(
                    """CREATE INDEX "IX_Books_Title_lower_trgm" ON "Books" USING gin (lower("Title") gin_trgm_ops);""");
                migrationBuilder.Sql(
                    """CREATE INDEX "IX_Books_Author_lower_trgm" ON "Books" USING gin (lower("Author") gin_trgm_ops);""");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (IsNpgsql(migrationBuilder))
            {
                migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Books_Title_lower_trgm";""");
                migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Books_Author_lower_trgm";""");
                // Leave the pg_trgm extension installed; other objects may depend on it.
            }

            migrationBuilder.DropIndex(
                name: "IX_Shelves_IsPublic_IsHidden",
                table: "Shelves");

            migrationBuilder.DropIndex(
                name: "IX_Books_Isbn13",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Isbn13",
                table: "Books");
        }

        private static bool IsNpgsql(MigrationBuilder migrationBuilder) =>
            string.Equals(migrationBuilder.ActiveProvider, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal);
    }
}
