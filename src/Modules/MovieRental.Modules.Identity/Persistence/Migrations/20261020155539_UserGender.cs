using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserGender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Gender",
                schema: "identity",
                table: "Users",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Gender",
                schema: "identity",
                table: "Users");
        }
    }
}
