using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compras.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RqLiberacionApartadoLinea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cant_liberada_apartado",
                schema: "compras",
                table: "requisicion_lineas",
                type: "numeric(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "cant_sin_compra",
                schema: "compras",
                table: "requisicion_lineas",
                type: "numeric(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "motivo_ultima_liberacion",
                schema: "compras",
                table: "requisicion_lineas",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cant_liberada_apartado",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.DropColumn(
                name: "cant_sin_compra",
                schema: "compras",
                table: "requisicion_lineas");

            migrationBuilder.DropColumn(
                name: "motivo_ultima_liberacion",
                schema: "compras",
                table: "requisicion_lineas");
        }
    }
}
