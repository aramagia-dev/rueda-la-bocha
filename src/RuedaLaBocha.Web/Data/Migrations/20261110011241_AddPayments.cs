using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuedaLaBocha.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReservaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MercadoPagoPaymentId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PreferenceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    RawJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MercadoPagoPaymentId",
                table: "Payments",
                column: "MercadoPagoPaymentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");
        }
    }
}
