using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectMessageActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PinnedMessageId",
                schema: "identity",
                table: "DirectThreads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequestDeclined",
                schema: "identity",
                table: "DirectThreads",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedById",
                schema: "identity",
                table: "DirectThreads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                schema: "identity",
                table: "DirectMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAtUtc",
                schema: "identity",
                table: "DirectMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Forwarded",
                schema: "identity",
                table: "DirectMessages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToId",
                schema: "identity",
                table: "DirectMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DirectMessageMarks",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reaction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Starred = table.Column<bool>(type: "bit", nullable: false),
                    Hidden = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectMessageMarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectMessageMarks_DirectMessages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "identity",
                        principalTable: "DirectMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectMessageMarks_MessageId_UserId",
                schema: "identity",
                table: "DirectMessageMarks",
                columns: new[] { "MessageId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectMessageMarks",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "PinnedMessageId",
                schema: "identity",
                table: "DirectThreads");

            migrationBuilder.DropColumn(
                name: "RequestDeclined",
                schema: "identity",
                table: "DirectThreads");

            migrationBuilder.DropColumn(
                name: "RequestedById",
                schema: "identity",
                table: "DirectThreads");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                schema: "identity",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "EditedAtUtc",
                schema: "identity",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "Forwarded",
                schema: "identity",
                table: "DirectMessages");

            migrationBuilder.DropColumn(
                name: "ReplyToId",
                schema: "identity",
                table: "DirectMessages");
        }
    }
}
