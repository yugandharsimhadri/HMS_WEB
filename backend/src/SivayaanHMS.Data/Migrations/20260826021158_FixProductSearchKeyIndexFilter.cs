using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <summary>
    /// Superseded, and deliberately empty.
    ///
    /// This was added to correct the filter on IX_Products_TenantId_SearchKey,
    /// which InitialCreate wrote as "IsDeleted" = 0 — SQLite's quoting, left
    /// over from the move. On SQL Server that is a string literal unless
    /// QUOTED_IDENTIFIER is ON, so `dotnet ef database update` created the
    /// index and a deployment running the generated script through sqlcmd did
    /// not: one statement failed, the rest carried on, and the migration was
    /// recorded as applied. A database that looked migrated had silently lost
    /// the guarantee that stops the same medicine being stocked twice.
    ///
    /// The correction belongs in InitialCreate rather than here, and was moved
    /// there, because the schema had never been deployed anywhere but a
    /// developer machine — there was no history to preserve and no database
    /// that would have taken the intermediate step. Fixing the origin means a
    /// first install gets a correct index from its only migration instead of
    /// building a broken one and repairing it.
    ///
    /// The file stays because deleting it would renumber nothing but would
    /// strand the local database, which already has this migration in
    /// __EFMigrationsHistory. It applies as a no-op.
    /// </summary>
    public partial class FixProductSearchKeyIndexFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
