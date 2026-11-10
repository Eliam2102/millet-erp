using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M177CobroCajaLiquidacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cobro_caja_id",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tarjeta_ultimos4",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_tarjeta",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cobro_caja_id",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt");

            migrationBuilder.DropColumn(
                name: "tarjeta_ultimos4",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt");

            migrationBuilder.DropColumn(
                name: "tipo_tarjeta",
                schema: "producto_terminado",
                table: "liquidacion_albaranes_pt");
        }
    }
}
