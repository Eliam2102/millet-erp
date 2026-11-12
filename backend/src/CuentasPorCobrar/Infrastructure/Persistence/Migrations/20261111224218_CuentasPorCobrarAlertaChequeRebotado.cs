using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CuentasPorCobrarAlertaChequeRebotado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_alerta_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "alerta_cartera");

            migrationBuilder.AddCheckConstraint(
                name: "ck_alerta_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "alerta_cartera",
                sql: "tipo IN (1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_alerta_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "alerta_cartera");

            migrationBuilder.AddCheckConstraint(
                name: "ck_alerta_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "alerta_cartera",
                sql: "tipo IN (1, 2, 3, 4)");
        }
    }
}
