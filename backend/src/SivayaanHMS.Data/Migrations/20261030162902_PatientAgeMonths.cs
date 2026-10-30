using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class PatientAgeMonths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AgeMonths",
                table: "Patients",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeMonths",
                table: "Patients");
        }
    }
}
