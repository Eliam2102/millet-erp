using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FacturaRecepcionElegida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "recepcion_id",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturas_proveedor_recepcion",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                column: "recepcion_id",
                filter: "recepcion_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_facturas_proveedor_recepcion",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "recepcion_id",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");
        }
    }
}
