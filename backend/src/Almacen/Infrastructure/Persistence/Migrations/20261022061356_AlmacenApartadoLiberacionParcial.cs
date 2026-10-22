using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlmacenApartadoLiberacionParcial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cantidad_liberada",
                schema: "almacen",
                table: "apartados_requisicion",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "motivo_liberacion",
                schema: "almacen",
                table: "apartados_requisicion",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cantidad_liberada",
                schema: "almacen",
                table: "apartados_requisicion");

            migrationBuilder.DropColumn(
                name: "motivo_liberacion",
                schema: "almacen",
                table: "apartados_requisicion");
        }
    }
}
