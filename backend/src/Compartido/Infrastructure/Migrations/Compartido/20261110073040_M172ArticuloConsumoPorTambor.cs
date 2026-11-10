using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class M172ArticuloConsumoPorTambor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "consumo_por_tambor_a_linea",
                schema: "compartido",
                table: "articulos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "kg_por_presentacion",
                schema: "compartido",
                table: "articulos",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consumo_por_tambor_a_linea",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropColumn(
                name: "kg_por_presentacion",
                schema: "compartido",
                table: "articulos");
        }
    }
}
