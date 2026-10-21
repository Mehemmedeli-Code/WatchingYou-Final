using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Cinema.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchingYou : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartnerRequests",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScreeningId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Partner = table.Column<int>(type: "int", nullable: false),
                    PeopleWanted = table.Column<int>(type: "int", nullable: false),
                    HasTickets = table.Column<bool>(type: "bit", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FilledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerRequests_Screenings_ScreeningId",
                        column: x => x.ScreeningId,
                        principalSchema: "cinema",
                        principalTable: "Screenings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartnerRequestMembers",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerRequestMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerRequestMembers_PartnerRequests_RequestId",
                        column: x => x.RequestId,
                        principalSchema: "cinema",
                        principalTable: "PartnerRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerRequestMembers_RequestId_UserId",
                schema: "cinema",
                table: "PartnerRequestMembers",
                columns: new[] { "RequestId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerRequests_ScreeningId_UserId",
                schema: "cinema",
                table: "PartnerRequests",
                columns: new[] { "ScreeningId", "UserId" },
                unique: true,
                filter: "[ClosedAtUtc] IS NULL AND [FilledAtUtc] IS NULL AND [CancelledAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerRequestMembers",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "PartnerRequests",
                schema: "cinema");
        }
    }
}
