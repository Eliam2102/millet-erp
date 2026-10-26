using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OcLineaMedidaVidrio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "alto_mm",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ancho_mm",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "numeric(12,3)",
                precision: 12,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "producto_aw_variante_id",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_oc_lineas_variante_medida",
                schema: "compras",
                table: "orden_compra_lineas",
                sql: "(producto_aw_variante_id IS NULL AND ancho_mm IS NULL AND alto_mm IS NULL) OR (producto_aw_variante_id IS NOT NULL AND ancho_mm IS NOT NULL AND ancho_mm > 0 AND alto_mm IS NOT NULL AND alto_mm > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_oc_lineas_variante_medida",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropColumn(
                name: "alto_mm",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropColumn(
                name: "ancho_mm",
                schema: "compras",
                table: "orden_compra_lineas");

            migrationBuilder.DropColumn(
                name: "producto_aw_variante_id",
                schema: "compras",
                table: "orden_compra_lineas");
        }
    }
}
