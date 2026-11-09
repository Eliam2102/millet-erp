using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M449ComprobacionFueraPolitica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "exceso_autorizado_en",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exceso_autorizado_por",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "exceso_comprobacion",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exceso_autorizado_en",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos");

            migrationBuilder.DropColumn(
                name: "exceso_autorizado_por",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos");

            migrationBuilder.DropColumn(
                name: "exceso_comprobacion",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos");
        }
    }
}
