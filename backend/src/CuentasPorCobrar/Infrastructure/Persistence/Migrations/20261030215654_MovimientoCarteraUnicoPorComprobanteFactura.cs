using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovimientoCarteraUnicoPorComprobanteFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_movimiento_cartera_origen_factura_tipo",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera",
                columns: new[] { "origen_comprobante_id", "factura_cartera_id", "tipo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_movimiento_cartera_origen_factura_tipo",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera");
        }
    }
}
