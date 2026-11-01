using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SivayaanHMS.Data.Migrations
{
    /// <inheritdoc />
    public partial class IpLinkedBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdmissionId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdmissionId",
                table: "ProcedureBills",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdmissionId",
                table: "LabOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AdmissionId",
                table: "DiagnosticBills",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_AdmissionId",
                table: "Sales",
                column: "AdmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureBills_AdmissionId",
                table: "ProcedureBills",
                column: "AdmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_LabOrders_AdmissionId",
                table: "LabOrders",
                column: "AdmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticBills_AdmissionId",
                table: "DiagnosticBills",
                column: "AdmissionId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiagnosticBills_Admissions_AdmissionId",
                table: "DiagnosticBills",
                column: "AdmissionId",
                principalTable: "Admissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LabOrders_Admissions_AdmissionId",
                table: "LabOrders",
                column: "AdmissionId",
                principalTable: "Admissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProcedureBills_Admissions_AdmissionId",
                table: "ProcedureBills",
                column: "AdmissionId",
                principalTable: "Admissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Admissions_AdmissionId",
                table: "Sales",
                column: "AdmissionId",
                principalTable: "Admissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiagnosticBills_Admissions_AdmissionId",
                table: "DiagnosticBills");

            migrationBuilder.DropForeignKey(
                name: "FK_LabOrders_Admissions_AdmissionId",
                table: "LabOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_ProcedureBills_Admissions_AdmissionId",
                table: "ProcedureBills");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Admissions_AdmissionId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_AdmissionId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_ProcedureBills_AdmissionId",
                table: "ProcedureBills");

            migrationBuilder.DropIndex(
                name: "IX_LabOrders_AdmissionId",
                table: "LabOrders");

            migrationBuilder.DropIndex(
                name: "IX_DiagnosticBills_AdmissionId",
                table: "DiagnosticBills");

            migrationBuilder.DropColumn(
                name: "AdmissionId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "AdmissionId",
                table: "ProcedureBills");

            migrationBuilder.DropColumn(
                name: "AdmissionId",
                table: "LabOrders");

            migrationBuilder.DropColumn(
                name: "AdmissionId",
                table: "DiagnosticBills");
        }
    }
}
