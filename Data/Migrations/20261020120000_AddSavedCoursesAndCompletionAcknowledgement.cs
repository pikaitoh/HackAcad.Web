using HackAcad.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackAcad.Web.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261020120000_AddSavedCoursesAndCompletionAcknowledgement")]
    public partial class AddSavedCoursesAndCompletionAcknowledgement : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedUtc",
                table: "Enrollments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SavedCourses",
                columns: table => new
                {
                    SavedCourseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    SavedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedCourses", x => x.SavedCourseId);
                    table.ForeignKey(
                        name: "FK_SavedCourses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SavedCourses_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "CourseId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCourses_CourseId",
                table: "SavedCourses",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedCourses_UserId_CourseId",
                table: "SavedCourses",
                columns: new[] { "UserId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedCourses_UserId_SavedUtc",
                table: "SavedCourses",
                columns: new[] { "UserId", "SavedUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SavedCourses");
            migrationBuilder.DropColumn(name: "FinishedUtc", table: "Enrollments");
        }
    }
}
