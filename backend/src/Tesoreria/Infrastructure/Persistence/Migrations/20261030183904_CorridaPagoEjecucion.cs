using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorridaPagoEjecucion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_ejecucion",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "referencia_bancaria",
                schema: "tesoreria",
                table: "corrida_pago_linea",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fecha_ejecucion",
                schema: "tesoreria",
                table: "corrida_pago_linea");

            migrationBuilder.DropColumn(
                name: "referencia_bancaria",
                schema: "tesoreria",
                table: "corrida_pago_linea");
        }
    }
}
