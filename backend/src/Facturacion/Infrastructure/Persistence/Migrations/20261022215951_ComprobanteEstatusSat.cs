using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComprobanteEstatusSat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "es_cancelable_sat",
                schema: "facturacion",
                table: "comprobante",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "estatus_sat",
                schema: "facturacion",
                table: "comprobante",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estatus_sat_consultado_en",
                schema: "facturacion",
                table: "comprobante",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_sucursal_fecha_timbrado",
                schema: "facturacion",
                table: "comprobante",
                columns: new[] { "sucursal_id", "fecha_timbrado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_comprobante_sucursal_fecha_timbrado",
                schema: "facturacion",
                table: "comprobante");

            migrationBuilder.DropColumn(
                name: "es_cancelable_sat",
                schema: "facturacion",
                table: "comprobante");

            migrationBuilder.DropColumn(
                name: "estatus_sat",
                schema: "facturacion",
                table: "comprobante");

            migrationBuilder.DropColumn(
                name: "estatus_sat_consultado_en",
                schema: "facturacion",
                table: "comprobante");
        }
    }
}
