using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Facturacion.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M233ExcepcionDiferenciaCampo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "campo",
                schema: "facturacion",
                table: "bandeja_excepcion_importacion",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "valor_esperado",
                schema: "facturacion",
                table: "bandeja_excepcion_importacion",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "valor_recibido",
                schema: "facturacion",
                table: "bandeja_excepcion_importacion",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "campo",
                schema: "facturacion",
                table: "bandeja_excepcion_importacion");

            migrationBuilder.DropColumn(
                name: "valor_esperado",
                schema: "facturacion",
                table: "bandeja_excepcion_importacion");

            migrationBuilder.DropColumn(
                name: "valor_recibido",
                schema: "facturacion",
                table: "bandeja_excepcion_importacion");
        }
    }
}
