using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Con11DiferenciaCambiariaRealizada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CON-11 (86e3kdypd): diferencia cambiaria realizada de pagos (#5) y cobros (#12) en moneda extranjera, y revaluación de cierre
            // de bancos y clientes. El catálogo de Contabilidad tiene una subcuenta de pérdida y utilidad por origen (clientes, proveedores,
            // bancos), así que hay un rol por origen. Nacen sin cuenta; Con11CuentasCambiarias les asigna la del catálogo si ya está cargado.
            // Sin cuenta, el motor manda el pago o cobro con diferencia a la bandeja (rol sin cuenta), nunca lo pierde.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-CON11', 'migration-CON11', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('PERDIDA_CAMBIARIA_REALIZADA_PROVEEDORES', 'Pérdida cambiaria realizada en pagos a proveedores en moneda extranjera (CON-11)'),
                    ('UTILIDAD_CAMBIARIA_REALIZADA_PROVEEDORES', 'Utilidad cambiaria realizada en pagos a proveedores en moneda extranjera (CON-11)'),
                    ('PERDIDA_CAMBIARIA_REALIZADA_CLIENTES', 'Pérdida cambiaria realizada en cobros de clientes en moneda extranjera (CON-11)'),
                    ('UTILIDAD_CAMBIARIA_REALIZADA_CLIENTES', 'Utilidad cambiaria realizada en cobros de clientes en moneda extranjera (CON-11)'),
                    ('PERDIDA_CAMBIARIA_NO_REALIZADA_BANCOS', 'Pérdida cambiaria no realizada de bancos, revaluación de cierre (CON-11)'),
                    ('UTILIDAD_CAMBIARIA_NO_REALIZADA_BANCOS', 'Utilidad cambiaria no realizada de bancos, revaluación de cierre (CON-11)'),
                    ('PERDIDA_CAMBIARIA_NO_REALIZADA_CLIENTES', 'Pérdida cambiaria no realizada de clientes, revaluación de cierre (CON-11)'),
                    ('UTILIDAD_CAMBIARIA_NO_REALIZADA_CLIENTES', 'Utilidad cambiaria no realizada de clientes, revaluación de cierre (CON-11)')
                ) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);
                """);
            // Versión nueva sobre la vigente de #5 y #12 (la anterior no se edita): las mismas líneas más la pérdida (cargo) y la utilidad
            // (abono) cambiaria realizada. Campo DiferenciaCambiaria: el motor asienta ahí lo que separa el TC de la factura del TC del pago.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       left(coalesce(v.descripcion, '') || ' · CON-11: diferencia cambiaria realizada (TC de la factura contra TC del pago)', 500),
                       v.lineas || jsonb_build_array(
                           jsonb_build_object('Orden', m.orden + 1, 'Rol', 'PERDIDA_CAMBIARIA_REALIZADA_' || o.origen, 'Lado', 'Cargo',
                               'Campo', 'DiferenciaCambiaria', 'Auxiliar', 'Ninguno', 'PorLinea', false, 'SoloServicios', NULL,
                               'ConCentroCosto', false, 'Concepto', 'Pérdida cambiaria realizada'),
                           jsonb_build_object('Orden', m.orden + 2, 'Rol', 'UTILIDAD_CAMBIARIA_REALIZADA_' || o.origen, 'Lado', 'Abono',
                               'Campo', 'DiferenciaCambiaria', 'Auxiliar', 'Ninguno', 'PorLinea', false, 'SoloServicios', NULL,
                               'ConCentroCosto', false, 'Concepto', 'Utilidad cambiaria realizada')),
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-CON11', 'migration-CON11', NULL
                FROM (SELECT DISTINCT ON (empresa_id, tipo_evento) *
                      FROM contabilidad.plantillas_contables
                      WHERE deleted_at IS NULL AND tipo_evento IN ('tesoreria.pago-factura-proveedor.aplicado.v1', 'tesoreria.pago-cliente.confirmado.v1')
                      ORDER BY empresa_id, tipo_evento, numero_version DESC) v
                CROSS JOIN LATERAL (SELECT coalesce(max((x->>'Orden')::int), 0) AS orden FROM jsonb_array_elements(v.lineas) x) m
                CROSS JOIN LATERAL (SELECT CASE v.tipo_evento WHEN 'tesoreria.pago-cliente.confirmado.v1' THEN 'CLIENTES' ELSE 'PROVEEDORES' END AS origen) o
                WHERE v.lineas::text NOT LIKE '%DiferenciaCambiaria%';
                """);


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-CON11';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-CON11' AND cuenta_id IS NULL;
                """);


        }
    }
}
