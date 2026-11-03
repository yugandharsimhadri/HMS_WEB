using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOpdValidDaysAndDoctorQualification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_PatientId",
                table: "Visits");

            migrationBuilder.AddColumn<Guid>(
                name: "FeeWaivedAgainstVisitId",
                table: "Visits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FreeFollowUpUntil",
                table: "Visits",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpdValidDays",
                table: "Doctors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Qualification",
                table: "Doctors",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visits_FeeWaivedAgainstVisitId",
                table: "Visits",
                column: "FeeWaivedAgainstVisitId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_PatientId_DoctorId_FreeFollowUpUntil",
                table: "Visits",
                columns: new[] { "PatientId", "DoctorId", "FreeFollowUpUntil" });

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_Visits_FeeWaivedAgainstVisitId",
                table: "Visits",
                column: "FeeWaivedAgainstVisitId",
                principalTable: "Visits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Visits_Visits_FeeWaivedAgainstVisitId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_FeeWaivedAgainstVisitId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_PatientId_DoctorId_FreeFollowUpUntil",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "FeeWaivedAgainstVisitId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "FreeFollowUpUntil",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "OpdValidDays",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "Qualification",
                table: "Doctors");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_PatientId",
                table: "Visits",
                column: "PatientId");
        }
    }
}
