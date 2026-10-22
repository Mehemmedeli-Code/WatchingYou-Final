using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Cinema.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShiftClosesOnce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Model only: CashShifts.ClosedAtUtc became a concurrency token, so closing writes
            // "WHERE ClosedAtUtc IS NULL" and two closes sent together cannot both succeed.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
