using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M096CancelacionCfdiSustituto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "autorizado_por",
                schema: "cuentas_por_pagar",
                table: "solicitudes_cancelacion_cfdi",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sustituto_cfdi_id",
                schema: "cuentas_por_pagar",
                table: "solicitudes_cancelacion_cfdi",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "autorizado_por",
                schema: "cuentas_por_pagar",
                table: "solicitudes_cancelacion_cfdi");

            migrationBuilder.DropColumn(
                name: "sustituto_cfdi_id",
                schema: "cuentas_por_pagar",
                table: "solicitudes_cancelacion_cfdi");
        }
    }
}
