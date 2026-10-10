using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Rentals.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "rentals");

            migrationBuilder.CreateTable(
                name: "Rentals",
                schema: "rentals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovieId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovieTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PosterUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RentedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReturnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtensionCount = table.Column<int>(type: "int", nullable: false),
                    DailyPrice = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    BasePrice = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    LateFee = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DueSoonNotified = table.Column<bool>(type: "bit", nullable: false),
                    OverdueNotified = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rentals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                schema: "rentals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Plan = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CardBrand = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CardLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    ReminderSentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_DueAtUtc",
                schema: "rentals",
                table: "Rentals",
                column: "DueAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_UserId_MovieId",
                schema: "rentals",
                table: "Rentals",
                columns: new[] { "UserId", "MovieId" },
                unique: true,
                filter: "[ReturnedAtUtc] IS NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_UserId_ReturnedAtUtc",
                schema: "rentals",
                table: "Rentals",
                columns: new[] { "UserId", "ReturnedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId_EndsAtUtc",
                schema: "rentals",
                table: "Subscriptions",
                columns: new[] { "UserId", "EndsAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Rentals",
                schema: "rentals");

            migrationBuilder.DropTable(
                name: "Subscriptions",
                schema: "rentals");
        }
    }
}
