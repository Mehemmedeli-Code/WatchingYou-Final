using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OriginalsAndSocial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "identity",
                table: "Users",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            // Existing accounts get a placeholder handle they can change on the profile page; the
            // unique index below needs them distinct, and the id makes them so.
            migrationBuilder.Sql("UPDATE [identity].[Users] SET [Username] = 'user_' + LOWER(LEFT(REPLACE(CONVERT(nvarchar(36), [Id]), '-', ''), 10)) WHERE [Username] IS NULL");

            migrationBuilder.CreateTable(
                name: "Follows",
                schema: "identity",
                columns: table => new
                {
                    FollowerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FolloweeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => new { x.FollowerId, x.FolloweeId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                schema: "identity",
                table: "Users",
                column: "Username",
                unique: true,
                filter: "[Username] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FolloweeId",
                schema: "identity",
                table: "Follows",
                column: "FolloweeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Follows",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "identity",
                table: "Users");
        }
    }
}
