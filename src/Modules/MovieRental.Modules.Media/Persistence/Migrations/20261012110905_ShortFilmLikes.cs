using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Media.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShortFilmLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShortFilmLikes",
                schema: "media",
                columns: table => new
                {
                    ShortFilmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortFilmLikes", x => new { x.ShortFilmId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ShortFilmLikes_ShortFilms_ShortFilmId",
                        column: x => x.ShortFilmId,
                        principalSchema: "media",
                        principalTable: "ShortFilms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortFilmLikes",
                schema: "media");
        }
    }
}
