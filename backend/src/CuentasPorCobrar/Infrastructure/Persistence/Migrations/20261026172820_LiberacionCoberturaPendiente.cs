using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiberacionCoberturaPendiente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_decision_liberacion_regla",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_decision_liberacion_resultado",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.AddColumn<Guid>(
                name: "decision_origen_id",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "porcentaje_cobertura",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_decision_liberacion_resultado_fecha",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                columns: new[] { "resultado", "decidido_en" });

            migrationBuilder.CreateIndex(
                name: "ux_decision_liberacion_origen",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                column: "decision_origen_id",
                unique: true,
                filter: "decision_origen_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_decision_liberacion_cobertura_rango",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                sql: "porcentaje_cobertura IS NULL OR (porcentaje_cobertura >= 0 AND porcentaje_cobertura <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_decision_liberacion_origen_resolucion",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                sql: "decision_origen_id IS NULL OR resultado IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_decision_liberacion_pendiente_cobertura",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                sql: "resultado != 4 OR (regla_aplicada = 4 AND porcentaje_cobertura >= 50 AND porcentaje_cobertura < 100)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_decision_liberacion_regla",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                sql: "regla_aplicada IN (1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_decision_liberacion_resultado",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                sql: "resultado IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_decision_liberacion_resultado_fecha",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropIndex(
                name: "ux_decision_liberacion_origen",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_decision_liberacion_cobertura_rango",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_decision_liberacion_origen_resolucion",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_decision_liberacion_pendiente_cobertura",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_decision_liberacion_regla",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_decision_liberacion_resultado",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropColumn(
                name: "decision_origen_id",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropColumn(
                name: "motivo",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.DropColumn(
                name: "porcentaje_cobertura",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_decision_liberacion_regla",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                sql: "regla_aplicada IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_decision_liberacion_resultado",
                schema: "cuentas_por_cobrar",
                table: "decision_liberacion",
                sql: "resultado IN (1, 2, 3)");
        }
    }
}
