using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Cinema.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeatPaymentsByUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_UserId_Status_ExpiresAtUtc",
                schema: "cinema",
                table: "SeatPayments",
                columns: new[] { "UserId", "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SeatPayments_UserId_Status_ExpiresAtUtc",
                schema: "cinema",
                table: "SeatPayments");
        }
    }
}
