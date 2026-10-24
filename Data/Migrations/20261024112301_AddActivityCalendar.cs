using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackAcad.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedUtc",
                table: "progress_records",
                type: "datetime(6)",
                nullable: true);

            // Existing rows only retain their most recent completion/update time.
            migrationBuilder.Sql(
                "UPDATE progress_records SET CompletedUtc = LastUpdatedDate WHERE IsCompleted = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_progress_records_EnrollmentId_CompletedUtc",
                table: "progress_records",
                columns: new[] { "EnrollmentId", "CompletedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_progress_records_EnrollmentId_CompletedUtc",
                table: "progress_records");

            migrationBuilder.DropColumn(
                name: "CompletedUtc",
                table: "progress_records");
        }
    }
}
