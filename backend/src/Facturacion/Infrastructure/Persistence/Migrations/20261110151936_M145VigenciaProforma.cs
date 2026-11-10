using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M145VigenciaProforma : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "vigente_hasta",
                schema: "facturacion",
                table: "proformas_pedido",
                type: "date",
                nullable: true);

            // M-145: las proformas ya generadas toman 15 días naturales desde su día de emisión en México.
            migrationBuilder.Sql("UPDATE facturacion.proformas_pedido SET vigente_hasta = ((generada_en AT TIME ZONE 'America/Mexico_City')::date + 15) WHERE vigente_hasta IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "vigente_hasta",
                schema: "facturacion",
                table: "proformas_pedido");
        }
    }
}
