using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductStrength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Strength",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StrengthValue",
                table: "Products",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Strength",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StrengthValue",
                table: "Products");
        }
    }
}
