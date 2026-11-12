using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M340ProformaFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "invalidada_en",
                schema: "facturacion",
                table: "proformas_conformidad",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invalidada_motivo",
                schema: "facturacion",
                table: "proformas_conformidad",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "proforma_pedido_id",
                schema: "facturacion",
                table: "factura_venta",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "invalidada_en",
                schema: "facturacion",
                table: "proformas_conformidad");

            migrationBuilder.DropColumn(
                name: "invalidada_motivo",
                schema: "facturacion",
                table: "proformas_conformidad");

            migrationBuilder.DropColumn(
                name: "proforma_pedido_id",
                schema: "facturacion",
                table: "factura_venta");
        }
    }
}
