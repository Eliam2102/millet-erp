using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PedimentoLineas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_doc_aduanero",
                schema: "facturacion",
                table: "factura_venta_linea",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "identificacion_mercancia",
                schema: "facturacion",
                table: "factura_venta_linea",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pedimento",
                schema: "facturacion",
                table: "factura_venta_linea",
                type: "character varying(21)",
                maxLength: 21,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requiere_pedimento",
                schema: "facturacion",
                table: "factura_venta_linea",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fecha_doc_aduanero",
                schema: "facturacion",
                table: "factura_venta_linea");

            migrationBuilder.DropColumn(
                name: "identificacion_mercancia",
                schema: "facturacion",
                table: "factura_venta_linea");

            migrationBuilder.DropColumn(
                name: "pedimento",
                schema: "facturacion",
                table: "factura_venta_linea");

            migrationBuilder.DropColumn(
                name: "requiere_pedimento",
                schema: "facturacion",
                table: "factura_venta_linea");
        }
    }
}
