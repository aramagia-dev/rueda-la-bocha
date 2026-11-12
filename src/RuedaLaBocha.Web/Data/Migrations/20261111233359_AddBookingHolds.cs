using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuedaLaBocha.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookingHolds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourtId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    AmountArs = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PadiLinkId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PadiPaymentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    QrCodeUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    BookingId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingHolds_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingHolds_Courts_CourtId",
                        column: x => x.CourtId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_BookingId",
                table: "BookingHolds",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_CourtId_Date_StartTime",
                table: "BookingHolds",
                columns: new[] { "CourtId", "Date", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_UserId_Date_Status",
                table: "BookingHolds",
                columns: new[] { "UserId", "Date", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingHolds");
        }
    }
}
