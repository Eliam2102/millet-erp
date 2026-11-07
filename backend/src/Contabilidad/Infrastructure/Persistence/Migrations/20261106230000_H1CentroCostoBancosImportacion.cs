using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// H1 pasos 3 y 4 (86e3z5w25): el área o equipo que el usuario captura en la alta desde el extracto (Tesorería) y al distribuir el
/// costo de un expediente (Comercio Exterior) llega a la póliza. Versión siguiente de cada plantilla vigente con <c>ConCentroCosto</c>
/// en la línea que carga a gasto: el concepto bancario (601 en las comisiones) y el gasto de importación (501). Las demás líneas
/// (IVA, bancos, inventario, gastos por aplicar) no cambian. Solo se agrega si la vigente aún no lleva el centro en esa línea; la
/// anterior no se edita. Solo datos: el modelo no cambia.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261106230000_H1CentroCostoBancosImportacion")]
public sealed class H1CentroCostoBancosImportacion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
               left(coalesce(v.descripcion, '') || ' · H1: área y equipo capturados en el movimiento', 500),
               n.lineas, 1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-H1-p34', 'migration-H1-p34', NULL
        FROM (SELECT DISTINCT ON (empresa_id, tipo_evento) *, CASE tipo_evento
                     WHEN 'tesoreria.movimiento-bancario.registrado.v1' THEN 'CONCEPTO_BANCARIO'
                     ELSE 'GASTO_IMPORTACION' END AS rol_gasto
              FROM contabilidad.plantillas_contables
              WHERE deleted_at IS NULL
                AND tipo_evento IN ('tesoreria.movimiento-bancario.registrado.v1', 'almacen.expediente.costo-aplicado.v1')
              ORDER BY empresa_id, tipo_evento, numero_version DESC) v
        CROSS JOIN LATERAL (
            SELECT jsonb_agg(CASE WHEN x->>'Rol' = v.rol_gasto AND x->>'Lado' = 'Cargo'
                                  THEN jsonb_set(x, '{ConCentroCosto}', 'true') ELSE x END ORDER BY o) AS lineas
            FROM jsonb_array_elements(v.lineas) WITH ORDINALITY AS t(x, o)
        ) n
        WHERE EXISTS (SELECT 1 FROM jsonb_array_elements(v.lineas) x
                      WHERE x->>'Rol' = v.rol_gasto AND x->>'Lado' = 'Cargo' AND coalesce((x->>'ConCentroCosto')::boolean, false) = false);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-H1-p34';
        """);
}
