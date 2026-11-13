using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M345CierreFicha : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ficha_cobranza_evento_tipo",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza_evento");

            migrationBuilder.AddColumn<bool>(
                name: "cerrada",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cerrada_en",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "explicacion_saldo",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "motivo_cierre",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "saldo_al_cerrar",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_ficha_cobranza_evento_tipo",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza_evento",
                sql: "tipo IN (1, 2, 3, 4, 5, 6, 7)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ficha_cobranza_motivo_cierre",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza",
                sql: "motivo_cierre IS NULL OR motivo_cierre IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ficha_cobranza_evento_tipo",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza_evento");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ficha_cobranza_motivo_cierre",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza");

            migrationBuilder.DropColumn(
                name: "cerrada",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza");

            migrationBuilder.DropColumn(
                name: "cerrada_en",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza");

            migrationBuilder.DropColumn(
                name: "explicacion_saldo",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza");

            migrationBuilder.DropColumn(
                name: "motivo_cierre",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza");

            migrationBuilder.DropColumn(
                name: "saldo_al_cerrar",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ficha_cobranza_evento_tipo",
                schema: "cuentas_por_cobrar",
                table: "ficha_cobranza_evento",
                sql: "tipo IN (1, 2, 3, 4, 5, 6)");
        }
    }
}
