using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HackAcad.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordChangedTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPasswordChangedUtc",
                table: "AspNetUsers",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPasswordChangedUtc",
                table: "AspNetUsers");
        }
    }
}
