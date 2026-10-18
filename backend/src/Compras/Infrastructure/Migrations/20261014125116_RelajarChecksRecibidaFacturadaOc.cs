using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RelajarChecksRecibidaFacturadaOc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_recibida_max",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "cantidad_facturada <= GREATEST(cantidad, cantidad_recibida)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_recibida_max",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "cantidad_recibida <= cantidad * 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los CHECK estrictos del Down fallarían con datos que ya excedan lo pedido (tolerancia por material).
            // Rollback con pérdida controlada: se recorta lo facturado y recibido a la cantidad de la línea; lo recortado NO se recupera.
            // Facturada primero: mientras los checks relajados siguen vigentes, facturada <= GREATEST(cantidad, recibida).
            migrationBuilder.Sql("UPDATE compras.orden_compra_lineas SET cantidad_facturada = LEAST(cantidad_facturada, cantidad);");
            migrationBuilder.Sql("UPDATE compras.orden_compra_lineas SET cantidad_recibida = LEAST(cantidad_recibida, cantidad);");

            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_recibida_max",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "cantidad_facturada <= cantidad");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_recibida_max",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "cantidad_recibida <= cantidad");
        }
    }
}
