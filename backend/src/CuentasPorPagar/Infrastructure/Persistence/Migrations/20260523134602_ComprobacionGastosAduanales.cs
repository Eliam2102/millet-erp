using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComprobacionGastosAduanales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "autorizado_por_nivel1",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_autorizacion_nivel1",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "numero_pedimento",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "autorizado_por_nivel1",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos");

            migrationBuilder.DropColumn(
                name: "fecha_autorizacion_nivel1",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos");

            migrationBuilder.DropColumn(
                name: "numero_pedimento",
                schema: "cuentas_por_pagar",
                table: "comprobaciones_gastos");
        }
    }
}
