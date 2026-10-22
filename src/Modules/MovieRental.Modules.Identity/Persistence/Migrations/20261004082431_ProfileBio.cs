using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfileBio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bio",
                schema: "identity",
                table: "Users",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bio",
                schema: "identity",
                table: "Users");
        }
    }
}
