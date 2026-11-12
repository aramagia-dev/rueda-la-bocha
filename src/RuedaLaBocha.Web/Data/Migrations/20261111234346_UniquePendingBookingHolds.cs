using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuedaLaBocha.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniquePendingBookingHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingHolds_CourtId_Date_StartTime",
                table: "BookingHolds");

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_CourtId_Date_StartTime",
                table: "BookingHolds",
                columns: new[] { "CourtId", "Date", "StartTime" },
                unique: true,
                filter: "Status = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookingHolds_CourtId_Date_StartTime",
                table: "BookingHolds");

            migrationBuilder.CreateIndex(
                name: "IX_BookingHolds_CourtId_Date_StartTime",
                table: "BookingHolds",
                columns: new[] { "CourtId", "Date", "StartTime" });
        }
    }
}
