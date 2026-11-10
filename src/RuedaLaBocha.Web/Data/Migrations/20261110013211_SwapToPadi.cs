using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RuedaLaBocha.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwapToPadi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_MercadoPagoPaymentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PreferenceId",
                table: "Payments");

            migrationBuilder.RenameColumn(
                name: "MercadoPagoPaymentId",
                table: "Payments",
                newName: "PaymentLinkId");

            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "Payments",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PadiPaymentId",
                table: "Payments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrCodeUrl",
                table: "Payments",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PadiPaymentId",
                table: "Payments",
                column: "PadiPaymentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_PadiPaymentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "PadiPaymentId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "QrCodeUrl",
                table: "Payments");

            migrationBuilder.RenameColumn(
                name: "PaymentLinkId",
                table: "Payments",
                newName: "MercadoPagoPaymentId");

            migrationBuilder.AddColumn<string>(
                name: "PreferenceId",
                table: "Payments",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_MercadoPagoPaymentId",
                table: "Payments",
                column: "MercadoPagoPaymentId",
                unique: true);
        }
    }
}
