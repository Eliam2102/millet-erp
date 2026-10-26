using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FmtFacturaDatosImpresion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "area_pie2",
                schema: "facturacion",
                table: "pedido_facturable_linea",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "peso_kg",
                schema: "facturacion",
                table: "pedido_facturable_linea",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia",
                schema: "facturacion",
                table: "pedido_facturable_linea",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "area_pie2",
                schema: "facturacion",
                table: "factura_venta_linea",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "peso_kg",
                schema: "facturacion",
                table: "factura_venta_linea",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia",
                schema: "facturacion",
                table: "factura_venta_linea",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "condiciones_pago",
                schema: "facturacion",
                table: "factura_venta",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "orden_compra_cliente",
                schema: "facturacion",
                table: "factura_venta",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "proforma",
                schema: "facturacion",
                table: "factura_venta",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia_cliente",
                schema: "facturacion",
                table: "factura_venta",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cadena_original_timbre",
                schema: "facturacion",
                table: "comprobante",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "no_certificado_emisor",
                schema: "facturacion",
                table: "comprobante",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "area_pie2",
                schema: "facturacion",
                table: "pedido_facturable_linea");

            migrationBuilder.DropColumn(
                name: "peso_kg",
                schema: "facturacion",
                table: "pedido_facturable_linea");

            migrationBuilder.DropColumn(
                name: "referencia",
                schema: "facturacion",
                table: "pedido_facturable_linea");

            migrationBuilder.DropColumn(
                name: "area_pie2",
                schema: "facturacion",
                table: "factura_venta_linea");

            migrationBuilder.DropColumn(
                name: "peso_kg",
                schema: "facturacion",
                table: "factura_venta_linea");

            migrationBuilder.DropColumn(
                name: "referencia",
                schema: "facturacion",
                table: "factura_venta_linea");

            migrationBuilder.DropColumn(
                name: "condiciones_pago",
                schema: "facturacion",
                table: "factura_venta");

            migrationBuilder.DropColumn(
                name: "orden_compra_cliente",
                schema: "facturacion",
                table: "factura_venta");

            migrationBuilder.DropColumn(
                name: "proforma",
                schema: "facturacion",
                table: "factura_venta");

            migrationBuilder.DropColumn(
                name: "referencia_cliente",
                schema: "facturacion",
                table: "factura_venta");

            migrationBuilder.DropColumn(
                name: "cadena_original_timbre",
                schema: "facturacion",
                table: "comprobante");

            migrationBuilder.DropColumn(
                name: "no_certificado_emisor",
                schema: "facturacion",
                table: "comprobante");
        }
    }
}
