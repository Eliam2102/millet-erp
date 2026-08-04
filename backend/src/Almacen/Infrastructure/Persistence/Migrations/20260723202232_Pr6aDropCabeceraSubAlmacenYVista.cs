using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Almacén-por-línea PR6a · M2 (C3). Retira el sub-almacén de la cabecera del
    /// movimiento y lo reemplaza por la vista <c>almacen.v_movimiento_sub_almacen</c>
    /// (una fila por movimiento, <c>DISTINCT ON</c>, sub derivado de la ubicación
    /// de la primera línea). Los 4 lectores (salida, recepción, reporte Alfak,
    /// diferencia de precio) hacen join a la vista.
    ///
    /// <para>Up: crea la vista → DROP FK → DROP índice viejo (sub+fecha) → DROP
    /// columna → CREATE índice nuevo (solo fecha, mismo nombre/filtro). Este M2
    /// NO toca el trigger (vive en M1); reparto de Down por corrección de F4.</para>
    ///
    /// <para>Down: re-crea la columna nullable, la re-deriva desde la ubicación
    /// de las líneas (vía la vista, aún viva), <b>falla ruidoso</b> si algún
    /// movimiento sin líneas impide derivar (no hay hoy), la promueve a NOT NULL,
    /// restaura índice y FK, y finalmente dropea la vista. El valor es
    /// re-derivable → rollback lossless. La receta también va en el body del PR.</para>
    /// </summary>
    public partial class Pr6aDropCabeceraSubAlmacenYVista : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vista: una fila por movimiento, sub de la primera línea (por
            // posición). El invariante MOVIMIENTO_MULTI_SUBALMACEN (trigger, M1)
            // garantiza que todas las líneas comparten sub, así que DISTINCT ON
            // no pierde información — sólo blinda contra fan-out en los joins.
            migrationBuilder.Sql(@"
CREATE VIEW almacen.v_movimiento_sub_almacen AS
SELECT DISTINCT ON (lm.movimiento_id)
       lm.movimiento_id,
       u.sub_almacen_id
FROM almacen.lineas_movimiento lm
JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
ORDER BY lm.movimiento_id, lm.posicion;
");

            migrationBuilder.DropForeignKey(
                name: "fk_movimientos_inventario_sub_almacenes_sub_almacen_id",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropIndex(
                name: "ix_movimientos_recepciones",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.DropColumn(
                name: "sub_almacen_id",
                schema: "almacen",
                table: "movimientos_inventario");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_recepciones",
                schema: "almacen",
                table: "movimientos_inventario",
                column: "fecha_movimiento",
                filter: "tipo = 0 AND estado = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Índice nuevo fuera antes de re-crear la columna.
            migrationBuilder.DropIndex(
                name: "ix_movimientos_recepciones",
                schema: "almacen",
                table: "movimientos_inventario");

            // Re-crear la columna NULLABLE y re-derivar desde la vista (aún
            // viva) — lossless porque el sub es derivable de la línea.
            migrationBuilder.Sql(
                "ALTER TABLE almacen.movimientos_inventario ADD COLUMN sub_almacen_id uuid;");
            migrationBuilder.Sql(@"
UPDATE almacen.movimientos_inventario m
SET sub_almacen_id = v.sub_almacen_id
FROM almacen.v_movimiento_sub_almacen v
WHERE v.movimiento_id = m.id;
");

            // Guard (corrección F4-5): un movimiento SIN líneas no tiene de dónde
            // derivar el sub. Hoy no existen; si aparecieran, fallar ruidoso en
            // vez de dejar el SET NOT NULL reventando opaco.
            migrationBuilder.Sql(@"
DO $$
DECLARE v_faltan INT;
BEGIN
  SELECT COUNT(*) INTO v_faltan
    FROM almacen.movimientos_inventario WHERE sub_almacen_id IS NULL;
  IF v_faltan > 0 THEN
    RAISE EXCEPTION
      'PR6A_DOWN_SUB_NO_DERIVABLE: % movimientos sin lineas no pueden re-derivar sub_almacen_id (rollback). Resolver manualmente antes de bajar M2.',
      v_faltan;
  END IF;
END $$;
");

            migrationBuilder.Sql(
                "ALTER TABLE almacen.movimientos_inventario ALTER COLUMN sub_almacen_id SET NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_movimientos_recepciones",
                schema: "almacen",
                table: "movimientos_inventario",
                columns: new[] { "sub_almacen_id", "fecha_movimiento" },
                filter: "tipo = 0 AND estado = 2");

            migrationBuilder.AddForeignKey(
                name: "fk_movimientos_inventario_sub_almacenes_sub_almacen_id",
                schema: "almacen",
                table: "movimientos_inventario",
                column: "sub_almacen_id",
                principalSchema: "almacen",
                principalTable: "sub_almacenes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // La vista ya cumplió su papel en la re-derivación; se retira.
            migrationBuilder.Sql("DROP VIEW almacen.v_movimiento_sub_almacen;");
        }
    }
}
