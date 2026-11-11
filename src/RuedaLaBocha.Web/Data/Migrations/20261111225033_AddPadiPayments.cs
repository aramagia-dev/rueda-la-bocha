using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuedaLaBocha.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPadiPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PadiPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookingId = table.Column<int>(type: "INTEGER", nullable: false),
                    PadiLinkId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PadiPaymentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    QrCodeUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AmountArs = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PadiPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PadiPayments_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PadiPayments_BookingId",
                table: "PadiPayments",
                column: "BookingId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PadiPayments");
        }
    }
}
