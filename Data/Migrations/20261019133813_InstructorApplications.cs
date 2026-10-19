using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackAcad.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InstructorApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_Teachers_TeacherId",
                table: "Courses");

            // Rename the role marker table: retain every existing owner and course relation.
            migrationBuilder.DropForeignKey(name: "FK_Teachers_AspNetUsers_UserId", table: "Teachers");
            migrationBuilder.RenameTable(name: "Teachers", newName: "Instructors");
            migrationBuilder.AddForeignKey(name: "FK_Instructors_AspNetUsers_UserId", table: "Instructors", column: "UserId", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.RenameColumn(
                name: "TeacherId",
                table: "Courses",
                newName: "InstructorId");

            migrationBuilder.RenameIndex(
                name: "IX_Courses_TeacherId",
                table: "Courses",
                newName: "IX_Courses_InstructorId");

            migrationBuilder.CreateTable(
                name: "InstructorApplications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    FullName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WorkEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TeachingTopics = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExperienceJson = table.Column<string>(type: "varchar(6000)", maxLength: 6000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SupportingInformation = table.Column<string>(type: "varchar(3000)", maxLength: 3000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmissionNote = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EvidencePdf = table.Column<byte[]>(type: "longblob", nullable: false),
                    EvidenceName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TeachingDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstructorApplications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InstructorReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ApplicationId = table.Column<long>(type: "bigint", nullable: false),
                    ReviewerId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewerName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReasonCategory = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerificationNotes = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerifiedYears = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RelevantTeaching = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExperienceVerified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EvidenceVerified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DetailsConsistent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReviewedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructorReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstructorReviews_InstructorApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "InstructorApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InstructorApplications_Status_SubmittedUtc",
                table: "InstructorApplications",
                columns: new[] { "Status", "SubmittedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InstructorApplications_UserId_Revision",
                table: "InstructorApplications",
                columns: new[] { "UserId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstructorReviews_ApplicationId",
                table: "InstructorReviews",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_Instructors_InstructorId",
                table: "Courses",
                column: "InstructorId",
                principalTable: "Instructors",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_Instructors_InstructorId",
                table: "Courses");

            migrationBuilder.DropTable(
                name: "InstructorReviews");

            migrationBuilder.DropForeignKey(name: "FK_Instructors_AspNetUsers_UserId", table: "Instructors");
            migrationBuilder.RenameTable(name: "Instructors", newName: "Teachers");
            migrationBuilder.AddForeignKey(name: "FK_Teachers_AspNetUsers_UserId", table: "Teachers", column: "UserId", principalTable: "AspNetUsers", principalColumn: "Id", onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "InstructorApplications");

            migrationBuilder.RenameColumn(
                name: "InstructorId",
                table: "Courses",
                newName: "TeacherId");

            migrationBuilder.RenameIndex(
                name: "IX_Courses_InstructorId",
                table: "Courses",
                newName: "IX_Courses_TeacherId");

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_Teachers_TeacherId",
                table: "Courses",
                column: "TeacherId",
                principalTable: "Teachers",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
