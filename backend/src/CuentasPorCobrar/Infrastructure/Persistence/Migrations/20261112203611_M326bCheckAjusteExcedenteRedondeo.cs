using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M326bCheckAjusteExcedenteRedondeo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_propuesta_aplicacion_ajuste_no_positivo",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.AddCheckConstraint(
                name: "ck_propuesta_aplicacion_ajuste_rango",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                sql: "ajuste_no_fiscal <= 0 OR (COALESCE(tipo_ajuste, 0) = 3 AND ajuste_no_fiscal <= 0.99)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_propuesta_aplicacion_ajuste_rango",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.AddCheckConstraint(
                name: "ck_propuesta_aplicacion_ajuste_no_positivo",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                sql: "ajuste_no_fiscal <= 0");
        }
    }
}
