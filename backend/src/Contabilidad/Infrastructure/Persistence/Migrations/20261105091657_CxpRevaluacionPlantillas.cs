using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CxpRevaluacionPlantillas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CXP-12 (86e3jvvad): revaluación de cierre de saldos por pagar en moneda extranjera. Los roles de pérdida y utilidad cambiaria
            // no realizadas nacen SIN cuenta (Contabilidad los mapea; sin ellos el motor manda el evento a la bandeja, nunca lo pierde).
            // Plantilla de la revaluación (fecha = último día del mes) y su reversa (día 1) con los lados contrarios; proveedor como auxiliar.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-CXP12', 'migration-CXP12', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('PERDIDA_CAMBIARIA_NO_REALIZADA', 'Pérdida cambiaria no realizada por revaluación de cierre (CXP-12)'),
                    ('UTILIDAD_CAMBIARIA_NO_REALIZADA', 'Utilidad cambiaria no realizada por revaluación de cierre (CXP-12)')
                ) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', 'AS', p.concepto, p.descripcion, p.lineas::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-CXP12', 'migration-CXP12', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('cuentas_por_pagar.revaluacion.registrada.v1', 'Revaluación de saldos en moneda extranjera',
                     'CXP-12: pérdida = cargo a pérdida cambiaria no realizada y abono al proveedor; utilidad al revés (por proveedor, TC del último día del mes)',
                     '[{"Orden": 1, "Rol": "PERDIDA_CAMBIARIA_NO_REALIZADA", "Lado": "Cargo", "Campo": "Perdida", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Pérdida cambiaria no realizada"}, {"Orden": 2, "Rol": "PROVEEDORES", "Lado": "Abono", "Campo": "Perdida", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Revaluación del proveedor"}, {"Orden": 3, "Rol": "PROVEEDORES", "Lado": "Cargo", "Campo": "Utilidad", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Revaluación del proveedor"}, {"Orden": 4, "Rol": "UTILIDAD_CAMBIARIA_NO_REALIZADA", "Lado": "Abono", "Campo": "Utilidad", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Utilidad cambiaria no realizada"}]'),
                    ('cuentas_por_pagar.revaluacion.revertida.v1', 'Reversa de la revaluación de saldos en moneda extranjera',
                     'CXP-12: reversa automática del día 1 del periodo siguiente, con los lados contrarios de la revaluación',
                     '[{"Orden": 1, "Rol": "PROVEEDORES", "Lado": "Cargo", "Campo": "Perdida", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Reversa de la revaluación del proveedor"}, {"Orden": 2, "Rol": "PERDIDA_CAMBIARIA_NO_REALIZADA", "Lado": "Abono", "Campo": "Perdida", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Reversa de la pérdida cambiaria no realizada"}, {"Orden": 3, "Rol": "UTILIDAD_CAMBIARIA_NO_REALIZADA", "Lado": "Cargo", "Campo": "Utilidad", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Reversa de la utilidad cambiaria no realizada"}, {"Orden": 4, "Rol": "PROVEEDORES", "Lado": "Abono", "Campo": "Utilidad", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Reversa de la revaluación del proveedor"}]')
                ) AS p(tipo_evento, concepto, descripcion, lineas)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-CXP12';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-CXP12' AND cuenta_id IS NULL;
                """);
        }
    }
}
