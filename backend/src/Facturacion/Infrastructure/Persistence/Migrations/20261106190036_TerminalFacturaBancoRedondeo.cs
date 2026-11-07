using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TerminalFacturaBancoRedondeo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "comision_factura",
                schema: "facturacion",
                table: "terminal_factura_banco",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "iva_factura",
                schema: "facturacion",
                table: "terminal_factura_banco",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "redondeo",
                schema: "facturacion",
                table: "terminal_factura_banco",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // 86e3zcbjx: las facturas ligadas antes cuadraban al centavo; comisión e IVA del CFDI = los de los cargos y sin redondeo.
            migrationBuilder.Sql("UPDATE facturacion.terminal_factura_banco SET comision_factura = comision, iva_factura = iva_comision WHERE comision_factura = 0 AND iva_factura = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comision_factura",
                schema: "facturacion",
                table: "terminal_factura_banco");

            migrationBuilder.DropColumn(
                name: "iva_factura",
                schema: "facturacion",
                table: "terminal_factura_banco");

            migrationBuilder.DropColumn(
                name: "redondeo",
                schema: "facturacion",
                table: "terminal_factura_banco");
        }
    }
}
