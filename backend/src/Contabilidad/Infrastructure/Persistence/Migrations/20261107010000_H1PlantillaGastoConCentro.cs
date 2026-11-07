using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// H1 paso 2 (86e3z5w25): nueva versión de la plantilla del gasto comprobado (caja chica, viáticos y tarjeta) con el centro de costo
/// del evento en la línea de cargo GASTO. El usuario captura el área (y el equipo si la cuenta es 600) cuando la regla C-03 lo exige;
/// la ubicación se deriva. Copia la versión vigente de cada empresa (respeta lo que Contabilidad haya ajustado) con la misma vigencia,
/// para que «Reprocesar» un rechazo ya use la versión con centro. Solo datos: el modelo no cambia.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261107010000_H1PlantillaGastoConCentro")]
public sealed class H1PlantillaGastoConCentro : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
               'H1: ' || v.descripcion || '; el gasto lleva el área o equipo capturado',
               (SELECT jsonb_agg(CASE WHEN l->>'Rol' = 'GASTO' AND l->>'Lado' = 'Cargo'
                                      THEN jsonb_set(l, '{ConCentroCosto}', 'true'::jsonb) ELSE l END ORDER BY o)
                FROM jsonb_array_elements(v.lineas::jsonb) WITH ORDINALITY AS a(l, o)),
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-H1b', 'migration-H1b', NULL
        FROM (SELECT DISTINCT ON (empresa_id) *
              FROM contabilidad.plantillas_contables
              WHERE tipo_evento = 'cuentas_por_pagar.gasto.registrado.v1' AND deleted_at IS NULL
              ORDER BY empresa_id, numero_version DESC) v
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables p
                          WHERE p.empresa_id = v.empresa_id AND p.created_by = 'migration-H1b');
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-H1b';");
}
