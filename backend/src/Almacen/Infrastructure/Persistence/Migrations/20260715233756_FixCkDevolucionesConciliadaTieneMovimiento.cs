using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixCkDevolucionesConciliadaTieneMovimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_devoluciones_prov_registrada_tiene_movimiento",
                schema: "almacen",
                table: "devoluciones_proveedor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_devoluciones_prov_registrada_tiene_movimiento",
                schema: "almacen",
                table: "devoluciones_proveedor",
                sql: "(estado IN (3, 4) AND movimiento_salida_id IS NOT NULL) OR (estado <> 3 AND estado <> 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_devoluciones_prov_registrada_tiene_movimiento",
                schema: "almacen",
                table: "devoluciones_proveedor");

            migrationBuilder.AddCheckConstraint(
                name: "ck_devoluciones_prov_registrada_tiene_movimiento",
                schema: "almacen",
                table: "devoluciones_proveedor",
                sql: "(estado = 3 AND movimiento_salida_id IS NOT NULL) OR (estado <> 3 AND estado <> 4)");
        }
    }
}
