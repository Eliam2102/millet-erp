using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cxp10DiotMonedaPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "moneda",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tipo_cambio",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pagos_proveedor_local_empresa_id_fecha_pago",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local",
                columns: new[] { "empresa_id", "fecha_pago" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pagos_proveedor_local_empresa_id_fecha_pago",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local");

            migrationBuilder.DropColumn(
                name: "moneda",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local");

            migrationBuilder.DropColumn(
                name: "tipo_cambio",
                schema: "cuentas_por_pagar",
                table: "pagos_proveedor_local");
        }
    }
}
