using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ComercioExterior.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreparacionDespachoExportacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "autorizacion_parcial_json",
                schema: "comercio_exterior",
                table: "contenedores",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "excepcion_sin_factura_json",
                schema: "comercio_exterior",
                table: "contenedores",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "preparado_para_facturar_en",
                schema: "comercio_exterior",
                table: "contenedores",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "autorizacion_parcial_json",
                schema: "comercio_exterior",
                table: "contenedores");

            migrationBuilder.DropColumn(
                name: "excepcion_sin_factura_json",
                schema: "comercio_exterior",
                table: "contenedores");

            migrationBuilder.DropColumn(
                name: "preparado_para_facturar_en",
                schema: "comercio_exterior",
                table: "contenedores");
        }
    }
}
