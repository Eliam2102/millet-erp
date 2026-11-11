using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M240ReversaAjusteCobro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimiento_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ajuste_revertido_en",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ajuste_revertido_por",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_reversa_ajuste",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "poliza_reversa_ajuste_id",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimiento_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera",
                sql: "tipo IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimiento_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera");

            migrationBuilder.DropColumn(
                name: "ajuste_revertido_en",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "ajuste_revertido_por",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "motivo_reversa_ajuste",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "poliza_reversa_ajuste_id",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimiento_cartera_tipo",
                schema: "cuentas_por_cobrar",
                table: "movimiento_cartera",
                sql: "tipo IN (1, 2)");
        }
    }
}
