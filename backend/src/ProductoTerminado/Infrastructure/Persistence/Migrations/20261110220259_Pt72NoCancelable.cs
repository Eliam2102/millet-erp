using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.ProductoTerminado.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pt72NoCancelable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "estado72at",
                schema: "producto_terminado",
                table: "pedidos_pt",
                type: "timestamp with time zone",
                nullable: true);

            // Con el código anterior el 72 salía al emitir la primera hoja (en_ruta_at): esos pedidos ya lo mandaron y no lo repiten.
            migrationBuilder.Sql(
                "UPDATE producto_terminado.pedidos_pt SET estado72at = en_ruta_at WHERE en_ruta_at IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "estado72at",
                schema: "producto_terminado",
                table: "pedidos_pt");
        }
    }
}
