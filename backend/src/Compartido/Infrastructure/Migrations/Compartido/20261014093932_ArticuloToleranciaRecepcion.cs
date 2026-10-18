using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Compartido.Infrastructure.Migrations.Compartido
{
    /// <inheritdoc />
    public partial class ArticuloToleranciaRecepcion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tolerancia_cantidad_porcentaje",
                schema: "compartido",
                table: "articulos",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_articulos_tolerancia_rango",
                schema: "compartido",
                table: "articulos",
                sql: "tolerancia_cantidad_porcentaje IS NULL OR (tolerancia_cantidad_porcentaje >= 0 AND tolerancia_cantidad_porcentaje <= 100)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_articulos_tolerancia_rango",
                schema: "compartido",
                table: "articulos");

            migrationBuilder.DropColumn(
                name: "tolerancia_cantidad_porcentaje",
                schema: "compartido",
                table: "articulos");
        }
    }
}
