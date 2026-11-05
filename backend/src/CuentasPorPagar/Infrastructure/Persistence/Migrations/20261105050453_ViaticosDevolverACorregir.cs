using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ViaticosDevolverACorregir : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "comentario_devolucion",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "devuelto_por",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_devolucion",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "comentario_devolucion",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos");

            migrationBuilder.DropColumn(
                name: "devuelto_por",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos");

            migrationBuilder.DropColumn(
                name: "fecha_devolucion",
                schema: "cuentas_por_pagar",
                table: "solicitudes_viaticos");
        }
    }
}
