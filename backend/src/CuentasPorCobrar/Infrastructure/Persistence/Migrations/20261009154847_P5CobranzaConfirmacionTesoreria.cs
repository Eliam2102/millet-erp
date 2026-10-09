using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P5CobranzaConfirmacionTesoreria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "movimiento_bancario_id",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "propuesto_por",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "repp_timbrado",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "saldo_a_favor_por_identificar",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "movimiento_bancario_id",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "propuesto_por",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "repp_timbrado",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");

            migrationBuilder.DropColumn(
                name: "saldo_a_favor_por_identificar",
                schema: "cuentas_por_cobrar",
                table: "propuesta_aplicacion_pago");
        }
    }
}
