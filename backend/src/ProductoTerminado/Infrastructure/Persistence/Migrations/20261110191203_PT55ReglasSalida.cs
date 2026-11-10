using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PT55ReglasSalida : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "autorizacion_parcial_json",
                schema: "producto_terminado",
                table: "salidas_pt",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creada_por_id",
                schema: "producto_terminado",
                table: "salidas_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "excepcion_factura_json",
                schema: "producto_terminado",
                table: "salidas_pt",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "autorizacion_parcial_json",
                schema: "producto_terminado",
                table: "salidas_pt");

            migrationBuilder.DropColumn(
                name: "creada_por_id",
                schema: "producto_terminado",
                table: "salidas_pt");

            migrationBuilder.DropColumn(
                name: "excepcion_factura_json",
                schema: "producto_terminado",
                table: "salidas_pt");
        }
    }
}
