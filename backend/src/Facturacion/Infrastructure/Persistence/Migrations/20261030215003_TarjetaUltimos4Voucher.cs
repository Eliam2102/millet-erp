using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TarjetaUltimos4Voucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tarjeta_ultimos4",
                schema: "facturacion",
                table: "cobro_mostrador_forma_pago",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tarjeta_ultimos4",
                schema: "facturacion",
                table: "caja_movimiento",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tarjeta_ultimos4",
                schema: "facturacion",
                table: "cobro_mostrador_forma_pago");

            migrationBuilder.DropColumn(
                name: "tarjeta_ultimos4",
                schema: "facturacion",
                table: "caja_movimiento");
        }
    }
}
