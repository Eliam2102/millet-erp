using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M326RedondeoCobros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M-326: el cobro corto de hasta $0.99 se contabiliza en el rol REDONDEO (sin autorización). Se asegura el rol (sin cuenta: la
            // asigna Contabilidad; ya existe por la terminal) y se agrega una versión de la plantilla #12 con la línea REDONDEO, solo si la
            // vigente no la trae (no pisa una versión editada por Contabilidad). No toca la plantilla de la terminal.
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, 'REDONDEO', 'Redondeo de hasta $0.99 en cobros y pagos (sugerida: 703.21.00.00 OTROS GASTOS)',
                       NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M326', 'migration-M326', NULL
                FROM compartido.empresas e
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta r WHERE r.empresa_id = e.id AND r.codigo = 'REDONDEO');

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       'Plantilla #12 (M-326): redondeo de hasta $0.99 del cobro corto a la cuenta REDONDEO',
                       v.lineas::jsonb || '[{"Orden": 5, "Rol": "REDONDEO", "Lado": "Cargo", "Campo": "AjusteRedondeo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Redondeo del cobro"}]'::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-M326', 'migration-M326', NULL
                FROM (SELECT DISTINCT ON (empresa_id) *
                      FROM contabilidad.plantillas_contables
                      WHERE tipo_evento = 'tesoreria.pago-cliente.confirmado.v1' AND deleted_at IS NULL
                      ORDER BY empresa_id, numero_version DESC) v
                WHERE v.lineas::text LIKE '%"Aplicado"%' AND v.lineas::text NOT LIKE '%AjusteRedondeo%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-M326';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-M326' AND cuenta_id IS NULL;
                """);
        }
    }
}
