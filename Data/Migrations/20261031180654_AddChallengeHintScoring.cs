using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackAcad.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChallengeHintScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "challenge_hints",
                columns: table => new
                {
                    ChallengeHintId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ChallengeId = table.Column<int>(type: "int", nullable: false),
                    HintText = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    DeductionPoints = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_hints", x => x.ChallengeHintId);
                    table.ForeignKey(
                        name: "FK_challenge_hints_challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "challenges",
                        principalColumn: "ChallengeId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "challenge_score_awards",
                columns: table => new
                {
                    ChallengeScoreAwardId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChallengeId = table.Column<int>(type: "int", nullable: false),
                    MaximumPointsSnapshot = table.Column<int>(type: "int", nullable: false),
                    TotalDeductionSnapshot = table.Column<int>(type: "int", nullable: false),
                    AwardedPoints = table.Column<int>(type: "int", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_score_awards", x => x.ChallengeScoreAwardId);
                    table.ForeignKey(
                        name: "FK_challenge_score_awards_challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "challenges",
                        principalColumn: "ChallengeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_challenge_score_awards_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "challenge_hint_unlocks",
                columns: table => new
                {
                    ChallengeHintUnlockId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChallengeHintId = table.Column<int>(type: "int", nullable: false),
                    PointsDeducted = table.Column<int>(type: "int", nullable: false),
                    UnlockedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_hint_unlocks", x => x.ChallengeHintUnlockId);
                    table.ForeignKey(
                        name: "FK_challenge_hint_unlocks_challenge_hints_ChallengeHintId",
                        column: x => x.ChallengeHintId,
                        principalTable: "challenge_hints",
                        principalColumn: "ChallengeHintId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_challenge_hint_unlocks_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_hint_unlocks_ChallengeHintId",
                table: "challenge_hint_unlocks",
                column: "ChallengeHintId");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_hint_unlocks_UserId_ChallengeHintId",
                table: "challenge_hint_unlocks",
                columns: new[] { "UserId", "ChallengeHintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_challenge_hints_ChallengeId_DisplayOrder",
                table: "challenge_hints",
                columns: new[] { "ChallengeId", "DisplayOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_challenge_score_awards_ChallengeId",
                table: "challenge_score_awards",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_score_awards_UserId_ChallengeId",
                table: "challenge_score_awards",
                columns: new[] { "UserId", "ChallengeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge_hint_unlocks");

            migrationBuilder.DropTable(
                name: "challenge_score_awards");

            migrationBuilder.DropTable(
                name: "challenge_hints");
        }
    }
}
