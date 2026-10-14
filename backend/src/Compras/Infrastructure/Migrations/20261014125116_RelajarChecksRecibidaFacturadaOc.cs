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
