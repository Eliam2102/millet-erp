using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Tesoreria.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PagoACuentaAutorizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "autorizado_en",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "autorizado_por",
                schema: "tesoreria",
                table: "movimiento_bancario",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "autorizado_en",
                schema: "tesoreria",
                table: "movimiento_bancario");

            migrationBuilder.DropColumn(
                name: "autorizado_por",
                schema: "tesoreria",
                table: "movimiento_bancario");
        }
    }
}
