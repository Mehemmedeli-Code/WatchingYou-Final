using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MovieRental.Modules.Cinema.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cinema");

            migrationBuilder.CreateTable(
                name: "CashShifts",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashierName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    CountedCash = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Variance = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConcessionItems",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CostPrice = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Stock = table.Column<int>(type: "int", nullable: false),
                    LowStockThreshold = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConcessionItems", x => x.Id);
                    table.CheckConstraint("CK_Concession_Stock", "[Stock] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ConcessionSales",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Tender = table.Column<int>(type: "int", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConcessionSales", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FilmDeals",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovieId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovieTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Distributor = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SharePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmDeals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyEntries",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodes",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PercentOff = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    AmountOff = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    MinSubtotal = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    Redemptions = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodes", x => x.Id);
                    table.CheckConstraint("CK_Promo_Redemptions", "[MaxRedemptions] IS NULL OR [Redemptions] <= [MaxRedemptions]");
                });

            migrationBuilder.CreateTable(
                name: "TicketTypes",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    NameAz = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NameRu = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NameTr = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PercentOfBase = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Venues",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    City = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Latitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConcessionSaleLines",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConcessionSaleLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConcessionSaleLines_ConcessionSales_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "cinema",
                        principalTable: "ConcessionSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Halls",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    SeatsPerRow = table.Column<int>(type: "int", nullable: false),
                    BlockColumns = table.Column<int>(type: "int", nullable: false),
                    AisleWidth = table.Column<double>(type: "float", nullable: false),
                    RowPitch = table.Column<double>(type: "float", nullable: false),
                    RowRise = table.Column<double>(type: "float", nullable: false),
                    SeatWidth = table.Column<double>(type: "float", nullable: false),
                    RoomWidth = table.Column<double>(type: "float", nullable: false),
                    RoomDepth = table.Column<double>(type: "float", nullable: false),
                    RoomHeight = table.Column<double>(type: "float", nullable: false),
                    ScreenWidth = table.Column<double>(type: "float", nullable: false),
                    ScreenHeight = table.Column<double>(type: "float", nullable: false),
                    ScreenCurveRadius = table.Column<double>(type: "float", nullable: false),
                    FirstRowDistance = table.Column<double>(type: "float", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Halls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Halls_Venues_VenueId",
                        column: x => x.VenueId,
                        principalSchema: "cinema",
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Screenings",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovieId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovieTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    HallId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Hall = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    SeatsPerRow = table.Column<int>(type: "int", nullable: false),
                    SeatPrice = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    AudioLanguage = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    SubtitleLanguage = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RefundWindowHours = table.Column<int>(type: "int", nullable: true),
                    RefundFeePercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    RefundNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screenings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Screenings_Halls_HallId",
                        column: x => x.HallId,
                        principalSchema: "cinema",
                        principalTable: "Halls",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SeatPayments",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScreeningId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PromoCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PromoDiscount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PointsRedeemed = table.Column<int>(type: "int", nullable: false),
                    PointsDiscount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PointsEarned = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<int>(type: "int", nullable: false),
                    Tender = table.Column<int>(type: "int", nullable: true),
                    ShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalSessionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExternalPaymentId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Brand = table.Column<int>(type: "int", nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    CardHolder = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Salt = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    RefundReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatPayments_Screenings_ScreeningId",
                        column: x => x.ScreeningId,
                        principalSchema: "cinema",
                        principalTable: "Screenings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeatBookings",
                schema: "cinema",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScreeningId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Row = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    PricePaid = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TicketType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedInByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatBookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatBookings_Screenings_ScreeningId",
                        column: x => x.ScreeningId,
                        principalSchema: "cinema",
                        principalTable: "Screenings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeatBookings_SeatPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "cinema",
                        principalTable: "SeatPayments",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_CashierId",
                schema: "cinema",
                table: "CashShifts",
                column: "CashierId",
                unique: true,
                filter: "[ClosedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConcessionSaleLines_SaleId",
                schema: "cinema",
                table: "ConcessionSaleLines",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_ConcessionSales_CreatedAtUtc",
                schema: "cinema",
                table: "ConcessionSales",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConcessionSales_ShiftId",
                schema: "cinema",
                table: "ConcessionSales",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_FilmDeals_MovieId",
                schema: "cinema",
                table: "FilmDeals",
                column: "MovieId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Halls_VenueId_Name",
                schema: "cinema",
                table: "Halls",
                columns: new[] { "VenueId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyEntries_UserId_CreatedAtUtc",
                schema: "cinema",
                table: "LoyaltyEntries",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_Code",
                schema: "cinema",
                table: "PromoCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Screenings_HallId",
                schema: "cinema",
                table: "Screenings",
                column: "HallId");

            migrationBuilder.CreateIndex(
                name: "IX_Screenings_StartsAtUtc",
                schema: "cinema",
                table: "Screenings",
                column: "StartsAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Screenings_StartsAtUtc_AudioLanguage",
                schema: "cinema",
                table: "Screenings",
                columns: new[] { "StartsAtUtc", "AudioLanguage" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatBookings_PaymentId",
                schema: "cinema",
                table: "SeatBookings",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatBookings_ScreeningId_Row_Number",
                schema: "cinema",
                table: "SeatBookings",
                columns: new[] { "ScreeningId", "Row", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_Reference",
                schema: "cinema",
                table: "SeatPayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_ScreeningId_Status_ExpiresAtUtc",
                schema: "cinema",
                table: "SeatPayments",
                columns: new[] { "ScreeningId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatPayments_ShiftId",
                schema: "cinema",
                table: "SeatPayments",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketTypes_Name",
                schema: "cinema",
                table: "TicketTypes",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Venues_Name",
                schema: "cinema",
                table: "Venues",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashShifts",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "ConcessionItems",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "ConcessionSaleLines",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "FilmDeals",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "LoyaltyEntries",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "PromoCodes",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "SeatBookings",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "TicketTypes",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "ConcessionSales",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "SeatPayments",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "Screenings",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "Halls",
                schema: "cinema");

            migrationBuilder.DropTable(
                name: "Venues",
                schema: "cinema");
        }
    }
}
