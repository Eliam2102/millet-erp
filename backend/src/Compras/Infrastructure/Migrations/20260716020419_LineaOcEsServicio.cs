using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LineaOcEsServicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_servicio",
                schema: "compras",
                table: "orden_compra_lineas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // GAP-9: backfill del snapshot para las líneas existentes cuyo
            // artículo es de naturaleza Servicio (compartido.articulos.
            // naturaleza = 1). Las líneas nuevas lo capturan al crearse.
            migrationBuilder.Sql("UPDATE compras.orden_compra_lineas l SET es_servicio = true FROM compartido.articulos a WHERE a.id = l.articulo_id AND a.naturaleza = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "es_servicio",
                schema: "compras",
                table: "orden_compra_lineas");
        }
    }
}
