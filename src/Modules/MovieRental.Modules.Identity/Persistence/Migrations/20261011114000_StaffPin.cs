using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffPin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StaffPinFailures",
                schema: "identity",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StaffPinHash",
                schema: "identity",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StaffPinLockedUntilUtc",
                schema: "identity",
                table: "Users",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StaffPinFailures",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StaffPinHash",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "StaffPinLockedUntilUtc",
                schema: "identity",
                table: "Users");
        }
    }
}
