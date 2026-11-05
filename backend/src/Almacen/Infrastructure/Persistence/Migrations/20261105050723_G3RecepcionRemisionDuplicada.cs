using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class G3RecepcionRemisionDuplicada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "motivo_duplicado",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remision",
                schema: "almacen",
                table: "movimientos_inventario",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "motivo_duplicado",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "remision",
                schema: "almacen",
                table: "movimientos_inventario");
        }
    }
}
