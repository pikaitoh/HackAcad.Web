using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackAcad.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "courses",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ReviewRevision",
                table: "courses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "courses",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReviewVersion",
                table: "courses",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedUtc",
                table: "courses",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedForReviewUtc",
                table: "courses",
                type: "datetime(6)",
                nullable: true);

            // Existing published courses remain available; unpublished courses need review.
            migrationBuilder.Sql("UPDATE `courses` SET `ReviewStatus` = CASE WHEN `IsPublished` = 1 THEN 'Approved' ELSE 'Draft' END, `ReviewVersion` = REPLACE(UUID(), '-', '')");

            migrationBuilder.CreateIndex(
                name: "IX_courses_ReviewStatus_SubmittedForReviewUtc",
                table: "courses",
                columns: new[] { "ReviewStatus", "SubmittedForReviewUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_courses_ReviewStatus_SubmittedForReviewUtc",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "ReviewRevision",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "ReviewVersion",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "ReviewedUtc",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "SubmittedForReviewUtc",
                table: "courses");
        }
    }
}
