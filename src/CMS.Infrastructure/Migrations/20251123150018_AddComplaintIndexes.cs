using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComplaintIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Complaints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "Complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Complaints",
                type: "numeric(10,7)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockExpiresAt",
                table: "Complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LockToken",
                table: "Complaints",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Complaints",
                type: "numeric(10,7)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "Complaints",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "Complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSize",
                table: "ComplaintAttachments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "IsScanned",
                table: "ComplaintAttachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MimeType",
                table: "ComplaintAttachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScanResult",
                table: "ComplaintAttachments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_CreatedOn",
                table: "Complaints",
                column: "CreatedOn");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_DepartmentId",
                table: "Complaints",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Priority",
                table: "Complaints",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_Complaints_Status",
                table: "Complaints",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintAuditLogs_Timestamp",
                table: "ComplaintAuditLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Complaints_CreatedOn",
                table: "Complaints");

            migrationBuilder.DropIndex(
                name: "IX_Complaints_DepartmentId",
                table: "Complaints");

            migrationBuilder.DropIndex(
                name: "IX_Complaints_Priority",
                table: "Complaints");

            migrationBuilder.DropIndex(
                name: "IX_Complaints_Status",
                table: "Complaints");

            migrationBuilder.DropIndex(
                name: "IX_ComplaintAuditLogs_Timestamp",
                table: "ComplaintAuditLogs");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "LockExpiresAt",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "LockToken",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "ComplaintAttachments");

            migrationBuilder.DropColumn(
                name: "IsScanned",
                table: "ComplaintAttachments");

            migrationBuilder.DropColumn(
                name: "MimeType",
                table: "ComplaintAttachments");

            migrationBuilder.DropColumn(
                name: "ScanResult",
                table: "ComplaintAttachments");
        }
    }
}
