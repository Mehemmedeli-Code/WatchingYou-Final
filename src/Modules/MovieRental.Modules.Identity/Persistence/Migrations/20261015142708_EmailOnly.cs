using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Identity.Persistence.Migrations
{
    /// <summary>SMS is gone: every code goes by e-mail, so the channel and the phone number (kept
    /// only to receive SMS codes) go with it.</summary>
    public partial class EmailOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Without the channel column an SMS code would pass for an e-mail one.
            migrationBuilder.Sql("DELETE FROM [identity].[VerificationCodes] WHERE [Channel] = 2;");

            migrationBuilder.DropIndex(
                name: "IX_VerificationCodes_UserId_Purpose_Channel_SentAtUtc",
                schema: "identity",
                table: "VerificationCodes");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "identity",
                table: "VerificationCodes");

            migrationBuilder.DropColumn(
                name: "IsPhoneConfirmed",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "identity",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationCodes_UserId_Purpose_SentAtUtc",
                schema: "identity",
                table: "VerificationCodes",
                columns: new[] { "UserId", "Purpose", "SentAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VerificationCodes_UserId_Purpose_SentAtUtc",
                schema: "identity",
                table: "VerificationCodes");

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                schema: "identity",
                table: "VerificationCodes",
                type: "int",
                nullable: false,
                defaultValue: 1);   // Email

            migrationBuilder.AddColumn<bool>(
                name: "IsPhoneConfirmed",
                schema: "identity",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "identity",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationCodes_UserId_Purpose_Channel_SentAtUtc",
                schema: "identity",
                table: "VerificationCodes",
                columns: new[] { "UserId", "Purpose", "Channel", "SentAtUtc" });
        }
    }
}
