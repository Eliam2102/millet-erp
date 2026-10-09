using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Almacen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P1AlmacenDocumentosValidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "linea_oc_id",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "linea_salida_origen_id",
                schema: "almacen",
                table: "lineas_movimiento",
                type: "uuid",
                nullable: true);
            migrationBuilder.CreateIndex("ix_lineas_movimiento_linea_oc_id", "lineas_movimiento", "linea_oc_id", "almacen");
            migrationBuilder.CreateIndex("ix_lineas_movimiento_linea_salida_origen_id", "lineas_movimiento", "linea_salida_origen_id", "almacen");

            // Compartido se migra primero (tools/migration-contexts.txt). Recalcula
            // únicamente vales pendientes, desde su fecha original y con los festivos configurados.
            migrationBuilder.Sql("""
                UPDATE almacen.movimientos_inventario m
                SET fecha_limite_regularizacion = (
                    SELECT (max(dia) + interval '1 day') AT TIME ZONE
                        (SELECT valor FROM compartido.parametros_globales WHERE clave = 'system.timezone-default')
                    FROM (
                        SELECT m.fecha_movimiento::timestamp + n * interval '1 day' AS dia
                        FROM generate_series(0, 365) n
                        WHERE extract(isodow FROM m.fecha_movimiento + n) BETWEEN 1 AND 5
                          AND NOT EXISTS (
                            SELECT 1 FROM compartido.parametros_globales p,
                                jsonb_array_elements_text(CASE WHEN p.clave = 'system.dias-festivos' THEN p.valor::jsonb ELSE '[]'::jsonb END) f(fecha)
                            WHERE p.clave = 'system.dias-festivos'
                              AND f.fecha::date = m.fecha_movimiento + n)
                        ORDER BY n LIMIT 2
                    ) habiles
                )
                WHERE m.tipo = 2 AND m.pendiente_regularizacion;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "linea_oc_id",
                schema: "almacen",
                table: "lineas_movimiento");

            migrationBuilder.DropColumn(
                name: "linea_salida_origen_id",
                schema: "almacen",
                table: "lineas_movimiento");
        }
    }
}
