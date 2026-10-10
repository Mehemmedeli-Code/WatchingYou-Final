using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OriginalsAndSocial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOriginal",
                schema: "catalog",
                table: "Movies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Rows seeded before the flag existed: the fourteen WatchingYou Originals.
            migrationBuilder.Sql("UPDATE [catalog].[Movies] SET [IsOriginal] = 1 WHERE [Title] IN (N'The Salt Road', N'Paper Lanterns', N'Quiet Signal', N'Cousins at the Border', N'Thirty-Six Frames', N'Low Tide Chorus', N'The Understudy', N'Dust and Copper', N'Blue Hour', N'Paperweight', N'The Long Count', N'Cold Open', N'Orbit of Small Things', N'Two Weeks in Şəki')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOriginal",
                schema: "catalog",
                table: "Movies");
        }
    }
}
