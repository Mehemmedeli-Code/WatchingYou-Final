using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HideOriginals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The made-up "WatchingYou Original" films leave the site. Soft-deleted, not removed:
            // past rentals and reviews keep their film, and the admin can restore one.
            migrationBuilder.Sql("UPDATE [catalog].[Movies] SET [IsDeleted] = 1, [DeletedAtUtc] = SYSUTCDATETIME() WHERE [IsOriginal] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [catalog].[Movies] SET [IsDeleted] = 0, [DeletedAtUtc] = NULL WHERE [IsOriginal] = 1");
        }
    }
}
