using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PedidoManualVentaDirecta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "clase_manual",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_emergencia",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia_pedido_aw",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "subtipo_venta_directa",
                schema: "facturacion",
                table: "pedido_facturable",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedido_clase_solo_manual",
                schema: "facturacion",
                table: "pedido_facturable",
                sql: "clase_manual IS NULL OR origen = 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedido_emergencia",
                schema: "facturacion",
                table: "pedido_facturable",
                sql: "clase_manual <> 2 OR (motivo_emergencia IS NOT NULL AND subtipo_venta_directa IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedido_vd_subtipo",
                schema: "facturacion",
                table: "pedido_facturable",
                sql: "clase_manual <> 1 OR subtipo_venta_directa IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pedido_clase_solo_manual",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedido_emergencia",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedido_vd_subtipo",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "clase_manual",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "motivo_emergencia",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "referencia_pedido_aw",
                schema: "facturacion",
                table: "pedido_facturable");

            migrationBuilder.DropColumn(
                name: "subtipo_venta_directa",
                schema: "facturacion",
                table: "pedido_facturable");
        }
    }
}
