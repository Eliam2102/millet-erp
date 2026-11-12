using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M080TipoCambioConfirmadoFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tipo_cambio_autorizado_por",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tipo_cambio_catalogo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tipo_cambio_autorizado_por",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "tipo_cambio_catalogo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");
        }
    }
}
