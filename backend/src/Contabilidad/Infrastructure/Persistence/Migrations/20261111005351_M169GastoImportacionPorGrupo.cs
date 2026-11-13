using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// M-169 (Contabilidad, 10-nov; Laura Cerón vía Guillermo Pantoja): la póliza del costo de importación carga el gasto por grupo
    /// contable del concepto de comercio exterior. Solo datos: el modelo no cambia.
    /// <list type="number">
    /// <item>Roles nuevos, sin cuenta: honorarios aduanales de persona física y de persona moral, fletes y maniobras, almacenajes y
    /// otros gastos de importación.</item>
    /// <item>Versión siguiente de la plantilla vigente de <c>almacen.expediente.costo-aplicado.v1</c> (la vigente no se edita): una
    /// línea de cargo por grupo, con el mismo <c>ConCentroCosto</c> que el cargo a GASTO_IMPORTACION; ese cargo queda para lo que
    /// llega sin grupo (eventos anteriores) y el abono sigue a GASTOS_IMPORTACION_POR_APLICAR. Las líneas en 0 no generan partida.</item>
    /// <item>Cuentas confirmadas por Contabilidad: GASTOS_IMPORTACION_POR_APLICAR → 201.07.00.00 GASTOS ADUANALES POR APLICAR;
    /// honorarios PF → 601.73.01.00 y PM → 601.73.02.00 (catálogo del 5-nov). Solo si la cuenta existe, está activa y es afectable, y
    /// el rol base sigue sin cuenta (no pisa lo que Contabilidad haya asignado). Sin la cuenta, el rol queda como estaba y
    /// <c>backend/scripts/contabilidad-asignar-cuentas-roles.sql</c> la asigna al importar el catálogo.</item>
    /// </list>
    /// Fletes y maniobras, almacenajes y otros gastos de importación quedan sin cuenta: Millet abrirá esas subcuentas en las pruebas.
    /// Mientras tanto un evento con esos grupos va a Rechazos («Rol sin cuenta») y se reprocesa sin duplicar al asignarla.
    /// </summary>
    public partial class M169GastoImportacionPorGrupo : Migration
    {
        /// <summary>Asigna las cuentas confirmadas a los roles sin cuenta. Idempotente; lo reutilizan las pruebas.</summary>
        public const string AsignarCuentasSql = """
            WITH confirmadas (rol, cuenta, motivo) AS (VALUES
                ('GASTOS_IMPORTACION_POR_APLICAR',  '201.07.00.00', 'GASTOS ADUANALES POR APLICAR'),
                ('GASTO_IMPORTACION_HONORARIOS_PF', '601.73.01.00', 'HONORARIOS ADUANALES PERSONAS FÍSICAS'),
                ('GASTO_IMPORTACION_HONORARIOS_PM', '601.73.02.00', 'HONORARIOS ADUANALES PERSONAS MORALES')),
            por_asignar AS (
                SELECT r.id AS rol_id, r.empresa_id, c.id AS cuenta_id, cf.cuenta, cf.motivo
                FROM contabilidad.roles_cuenta r
                JOIN confirmadas cf ON cf.rol = r.codigo
                JOIN contabilidad.cuentas_contables c
                  ON c.empresa_id = r.empresa_id AND c.codigo = cf.cuenta AND c.deleted_at IS NULL
                 AND c.estatus = 0 /* Activa */ AND c.tipo = 1 /* Afectable */
                WHERE r.deleted_at IS NULL AND r.sucursal_id IS NULL AND r.moneda IS NULL AND r.cuenta_id IS NULL
            ),
            asignados AS (
                UPDATE contabilidad.roles_cuenta r
                   SET cuenta_id = p.cuenta_id, version = r.version + 1,
                       updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-M169'
                  FROM por_asignar p
                 WHERE r.id = p.rol_id
                RETURNING r.id, r.empresa_id, p.cuenta_id, p.cuenta, p.motivo
            )
            INSERT INTO contabilidad.roles_cuenta_cambios
                (id, empresa_id, rol_cuenta_id, cuenta_anterior_id, cuenta_nueva_id, usuario, cambiado_en, motivo)
            SELECT gen_random_uuid(), a.empresa_id, a.id, NULL, a.cuenta_id, 'migration-M169', now() AT TIME ZONE 'UTC',
                   'M-169: ' || a.cuenta || ' ' || a.motivo || ' (Contabilidad, 10-nov)'
            FROM asignados a;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M169', 'migration-M169', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                ('GASTO_IMPORTACION_HONORARIOS_PF', 'Gasto de importación · honorarios aduanales de personas físicas'),
                ('GASTO_IMPORTACION_HONORARIOS_PM', 'Gasto de importación · honorarios aduanales de personas morales'),
                ('GASTO_IMPORTACION_FLETES_MANIOBRAS', 'Gasto de importación · fletes y maniobras; configure la cuenta antes de contabilizar'),
                ('GASTO_IMPORTACION_ALMACENAJES', 'Gasto de importación · almacenajes; configure la cuenta antes de contabilizar'),
                ('GASTO_IMPORTACION_OTROS', 'Gasto de importación · otros gastos de importación; configure la cuenta antes de contabilizar')
                ) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       left(coalesce(v.descripcion, '') || ' · M-169: gasto de importación por grupo contable del concepto', 500),
                       n.lineas, 1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M169', 'migration-M169', NULL
                FROM (SELECT DISTINCT ON (empresa_id) *
                      FROM contabilidad.plantillas_contables
                      WHERE deleted_at IS NULL AND tipo_evento = 'almacen.expediente.costo-aplicado.v1'
                      ORDER BY empresa_id, numero_version DESC) v
                CROSS JOIN LATERAL (
                    SELECT coalesce((SELECT (x->>'ConCentroCosto')::boolean FROM jsonb_array_elements(v.lineas::jsonb) x
                                     WHERE x->>'Rol' = 'GASTO_IMPORTACION' AND x->>'Lado' = 'Cargo' LIMIT 1), true) AS centro
                ) c
                CROSS JOIN LATERAL (
                    SELECT jsonb_agg(jsonb_set(t.x, '{Orden}', to_jsonb(t.n)) ORDER BY t.n) AS lineas
                    FROM (SELECT u.x, row_number() OVER (ORDER BY u.bloque, u.o) AS n
                          FROM (SELECT a.x, CASE WHEN a.x->>'Lado' = 'Cargo' THEN 1 ELSE 3 END AS bloque, a.o
                                FROM jsonb_array_elements(v.lineas::jsonb) WITH ORDINALITY AS a(x, o)
                                UNION ALL
                                SELECT jsonb_build_object('Orden', 0, 'Rol', g.rol, 'Lado', 'Cargo', 'Campo', g.campo, 'Auxiliar', 'Ninguno',
                                           'PorLinea', false, 'SoloServicios', NULL, 'ConCentroCosto', c.centro, 'Concepto', g.concepto,
                                           'RolPorClasificador', false), 2, g.o
                                FROM (VALUES
                                    (1::bigint, 'GASTO_IMPORTACION_HONORARIOS_PF', 'GastoHonorariosPf', 'Honorarios aduanales personas físicas'),
                                    (2::bigint, 'GASTO_IMPORTACION_HONORARIOS_PM', 'GastoHonorariosPm', 'Honorarios aduanales personas morales'),
                                    (3::bigint, 'GASTO_IMPORTACION_FLETES_MANIOBRAS', 'GastoFletesManiobras', 'Fletes y maniobras de importación'),
                                    (4::bigint, 'GASTO_IMPORTACION_ALMACENAJES', 'GastoAlmacenajes', 'Almacenajes de importación'),
                                    (5::bigint, 'GASTO_IMPORTACION_OTROS', 'GastoOtros', 'Otros gastos de importación')
                                ) AS g(o, rol, campo, concepto)) u) t
                ) n
                WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(v.lineas::jsonb) x WHERE x->>'Rol' = 'GASTO_IMPORTACION_HONORARIOS_PF');
                """);
            migrationBuilder.Sql(AsignarCuentasSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-M169';
                DELETE FROM contabilidad.roles_cuenta_cambios WHERE usuario = 'migration-M169';
                UPDATE contabilidad.roles_cuenta
                   SET cuenta_id = NULL, version = version + 1, updated_at = now() AT TIME ZONE 'UTC', updated_by = 'migration-M169-down'
                 WHERE updated_by = 'migration-M169' AND codigo = 'GASTOS_IMPORTACION_POR_APLICAR';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-M169';
                """);
        }
    }
}
