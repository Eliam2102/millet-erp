using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class H1CentroCostoGastos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "centro_costo_id",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "centro_costo_id",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "centro_costo_id",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_gastos",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "centro_costo_id",
                schema: "cuentas_por_pagar",
                table: "movimientos_tarjeta_credito");

            migrationBuilder.DropColumn(
                name: "centro_costo_id",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_viaticos");

            migrationBuilder.DropColumn(
                name: "centro_costo_id",
                schema: "cuentas_por_pagar",
                table: "lineas_comprobacion_gastos");
        }
    }
}
