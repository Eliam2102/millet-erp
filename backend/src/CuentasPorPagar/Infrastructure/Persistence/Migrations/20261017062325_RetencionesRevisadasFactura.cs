using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetencionesRevisadasFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retenciones_revisadas_en",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "retenciones_revisadas_por",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retenciones_revision_motivo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retenciones_revisadas_en",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "retenciones_revisadas_por",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");

            migrationBuilder.DropColumn(
                name: "retenciones_revision_motivo",
                schema: "cuentas_por_pagar",
                table: "facturas_proveedor");
        }
    }
}
