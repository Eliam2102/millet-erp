using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P23BrechasAuditoria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_minima_ce",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_devolucion_ce_movimiento",
                schema: "tesoreria",
                table: "deposito_confirmacion",
                column: "movimiento_id",
                unique: true,
                filter: "devolucion_ce_id IS NOT NULL AND movimiento_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_devolucion_ce_movimiento",
                schema: "tesoreria",
                table: "deposito_confirmacion");

            migrationBuilder.DropColumn(
                name: "fecha_minima_ce",
                schema: "tesoreria",
                table: "deposito_confirmacion");
        }
    }
}
