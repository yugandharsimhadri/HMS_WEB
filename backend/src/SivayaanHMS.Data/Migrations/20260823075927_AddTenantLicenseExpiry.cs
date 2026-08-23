using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLicenseExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LicenseExpiresOn",
                table: "Tenants",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LicenseExpiresOn",
                table: "Tenants");
        }
    }
}
