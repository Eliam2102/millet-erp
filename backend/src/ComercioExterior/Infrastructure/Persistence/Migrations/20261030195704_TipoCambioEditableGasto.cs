using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TipoCambioEditableGasto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "diferencia_tipo_cambio_pct",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "numeric(20,2)",
                precision: 20,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nota_tipo_cambio",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tipo_cambio_modificado_por",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tipo_cambio_sugerido",
                schema: "comercio_exterior",
                table: "gastos_expediente",
                type: "numeric(20,6)",
                precision: 20,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "diferencia_tipo_cambio_pct",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "nota_tipo_cambio",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "tipo_cambio_modificado_por",
                schema: "comercio_exterior",
                table: "gastos_expediente");

            migrationBuilder.DropColumn(
                name: "tipo_cambio_sugerido",
                schema: "comercio_exterior",
                table: "gastos_expediente");
        }
    }
}
