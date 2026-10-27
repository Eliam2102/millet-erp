using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UuidUnicoFacturasYViaticosPendientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_linea_via_uuid_pendiente",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos",
                column: "uuid_cfdi",
                unique: true,
                filter: "uuid_cfdi IS NOT NULL AND factura_proveedor_id IS NULL");

            // Índice de expresión (EF no lo modela): UUID único sin distinguir mayúsculas entre facturas vivas
            // (estado 5 = Cancelada queda fuera: su CFDI puede recapturarse). Es la autoridad si el chequeo de la app se salta.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_facturas_proveedor_uuid_vivo ON cuentas_por_pagar.facturas_proveedor (upper(uuid_cfdi)) " +
                "WHERE uuid_cfdi IS NOT NULL AND estado <> 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS cuentas_por_pagar.ux_facturas_proveedor_uuid_vivo;");

            migrationBuilder.DropIndex(
                name: "ux_linea_via_uuid_pendiente",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos");
        }
    }
}
