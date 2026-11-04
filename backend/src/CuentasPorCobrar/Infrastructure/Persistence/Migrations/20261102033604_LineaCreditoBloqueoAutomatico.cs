using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LineaCreditoBloqueoAutomatico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bloqueo_automatico",
                schema: "cuentas_por_cobrar",
                table: "linea_credito",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "desbloqueo_manual_en",
                schema: "cuentas_por_cobrar",
                table: "linea_credito",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_desbloqueo",
                schema: "cuentas_por_cobrar",
                table: "linea_credito",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            // C7.5: los bloqueos que ya puso la evaluación de vencimientos
            // (motivo «Auto-bloqueo: …») se marcan como automáticos para que
            // se levanten solos al pagar.
            migrationBuilder.Sql(
                "UPDATE cuentas_por_cobrar.linea_credito SET bloqueo_automatico = true " +
                "WHERE estado = 2 AND motivo_bloqueo LIKE 'Auto-bloqueo:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bloqueo_automatico",
                schema: "cuentas_por_cobrar",
                table: "linea_credito");

            migrationBuilder.DropColumn(
                name: "desbloqueo_manual_en",
                schema: "cuentas_por_cobrar",
                table: "linea_credito");

            migrationBuilder.DropColumn(
                name: "motivo_desbloqueo",
                schema: "cuentas_por_cobrar",
                table: "linea_credito");
        }
    }
}
