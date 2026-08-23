using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class VisitFeePaidAndFollowUpIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Visits_FeePaidOn",
                table: "Visits",
                column: "FeePaidOn");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_FollowUpOn",
                table: "Visits",
                column: "FollowUpOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_FeePaidOn",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_FollowUpOn",
                table: "Visits");
        }
    }
}
