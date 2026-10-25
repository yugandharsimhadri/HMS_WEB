using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class PasswordResetTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordResetTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChallengeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecoyAttempts = table.Column<int>(type: "integer", nullable: false),
                    GrantHandle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ExpiresOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    VerifiedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedOn = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTransactions_TokenHash",
                table: "PasswordResetTransactions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTransactions_UserId_CreatedAt",
                table: "PasswordResetTransactions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetTransactions");
        }
    }
}
