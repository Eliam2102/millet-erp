using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P19SolicitudAnticipoAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "sucursal_id", schema: "cuentas_por_pagar", table: "anticipos_proveedor", type: "uuid", nullable: true);
            migrationBuilder.DropCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                sql: "(origen = 1 AND uuid_cfdi IS NOT NULL AND referencia_origen IS NULL) OR (origen IN (2, 3) AND uuid_cfdi IS NULL AND cfdi_recibido_id IS NULL AND referencia_origen IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "sucursal_id", schema: "cuentas_por_pagar", table: "anticipos_proveedor");
            migrationBuilder.DropCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_anticipo_origen",
                schema: "cuentas_por_pagar",
                table: "anticipos_proveedor",
                sql: "(origen = 1 AND uuid_cfdi IS NOT NULL AND referencia_origen IS NULL) OR (origen = 2 AND uuid_cfdi IS NULL AND cfdi_recibido_id IS NULL AND referencia_origen IS NOT NULL)");
        }
    }
}
