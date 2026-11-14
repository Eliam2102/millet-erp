using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M147SaldoPortalCorrida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "evidencia_saldo_portal",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_consulta_portal",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "saldo_portal",
                schema: "tesoreria",
                table: "corrida_pago",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_corrida_pago_saldo_portal_no_negativo",
                schema: "tesoreria",
                table: "corrida_pago",
                sql: "saldo_portal IS NULL OR saldo_portal >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_corrida_pago_saldo_portal_no_negativo",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "evidencia_saldo_portal",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "fecha_consulta_portal",
                schema: "tesoreria",
                table: "corrida_pago");

            migrationBuilder.DropColumn(
                name: "saldo_portal",
                schema: "tesoreria",
                table: "corrida_pago");
        }
    }
}
