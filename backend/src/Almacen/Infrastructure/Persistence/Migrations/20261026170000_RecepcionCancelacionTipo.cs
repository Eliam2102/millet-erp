using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecepcionCancelacionTipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 11");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.AddCheckConstraint(
                name: "ck_movimientos_tipo",
                schema: "almacen",
                table: "movimientos_inventario",
                sql: "tipo BETWEEN 0 AND 10");
        }
    }
}
