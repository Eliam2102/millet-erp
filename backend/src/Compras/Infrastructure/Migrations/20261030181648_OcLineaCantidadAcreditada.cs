using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OcLineaCantidadAcreditada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_acreditada",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "numeric(15,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_acreditada_nn",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "cantidad_acreditada >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "cantidad_facturada <= GREATEST(cantidad, cantidad_recibida) + cantidad_acreditada");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_acreditada_nn",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropColumn(
                name: "cantidad_acreditada",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_facturada_max",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "cantidad_facturada <= GREATEST(cantidad, cantidad_recibida)");
        }
    }
}
