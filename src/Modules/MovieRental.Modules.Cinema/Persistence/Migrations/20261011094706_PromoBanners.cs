using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Cinema.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PromoBanners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromoBanners",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Theme = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Subline = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    PromoCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoBanners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromoBanners_Theme",
                schema: "cinema",
                table: "PromoBanners",
                column: "Theme",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromoBanners",
                schema: "cinema");
        }
    }
}
