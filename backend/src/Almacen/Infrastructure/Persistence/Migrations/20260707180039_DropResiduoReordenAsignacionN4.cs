using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropResiduoReordenAsignacionN4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_asignaciones_max_no_menor_min",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_asignaciones_niveles_no_negativos",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_asignaciones_objetivo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");

            migrationBuilder.DropColumn(
                name: "auto_requisicion",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");

            migrationBuilder.DropColumn(
                name: "maximo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");

            migrationBuilder.DropColumn(
                name: "minimo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");

            migrationBuilder.DropColumn(
                name: "objetivo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");

            migrationBuilder.DropColumn(
                name: "punto_reorden",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_requisicion",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "maximo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "minimo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<short>(
                name: "objetivo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<decimal>(
                name: "punto_reorden",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                type: "numeric(14,4)",
                precision: 14,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_asignaciones_max_no_menor_min",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                sql: "maximo >= minimo");

            migrationBuilder.AddCheckConstraint(
                name: "ck_asignaciones_niveles_no_negativos",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                sql: "minimo >= 0 AND maximo >= 0 AND punto_reorden >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_asignaciones_objetivo",
                schema: "almacen",
                table: "asignaciones_articulo_ubicacion",
                sql: "objetivo BETWEEN 0 AND 2");
        }
    }
}
