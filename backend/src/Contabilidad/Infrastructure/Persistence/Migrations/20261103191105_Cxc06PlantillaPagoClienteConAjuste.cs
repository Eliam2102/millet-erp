using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Cxc06PlantillaPagoClienteConAjuste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CXC-06 (86e3qddef): rol de la diferencia menor en cobro, SIN cuenta (Contabilidad la mapea; el de comisiones ya existe) y
            // plantilla #12 versión 2: bancos por el depósito + ajuste autorizado a la cuenta de su tipo · clientes por lo aplicado.
            // Solo se agrega la versión si la vigente aún no abona lo aplicado (no pisa una versión que Contabilidad ya haya editado).
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, 'DIFERENCIAS_EN_COBRO', 'Diferencias menores en cobro autorizadas como ajuste (CXC-06)',
                       NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-CXC06', 'migration-CXC06', NULL
                FROM compartido.empresas e
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta r WHERE r.empresa_id = e.id AND r.codigo = 'DIFERENCIAS_EN_COBRO');

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), v.empresa_id, v.tipo_evento, v.numero_version + 1, v.vigente_desde, v.tipo_operacion_clave, v.concepto,
                       'Plantilla #12 v2 (CXC-06): bancos y ajuste autorizado contra clientes por lo aplicado',
                       '[{"Orden": 1, "Rol": "BANCOS", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos"}, {"Orden": 2, "Rol": "COMISIONES_BANCARIAS", "Lado": "Cargo", "Campo": "AjusteComision", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Comisión bancaria del cobro"}, {"Orden": 3, "Rol": "DIFERENCIAS_EN_COBRO", "Lado": "Cargo", "Campo": "AjusteDiferencia", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Diferencia en cobro"}, {"Orden": 4, "Rol": "CLIENTES", "Lado": "Abono", "Campo": "Aplicado", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}]'::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-CXC06', 'migration-CXC06', NULL
                FROM (SELECT DISTINCT ON (empresa_id) *
                      FROM contabilidad.plantillas_contables
                      WHERE tipo_evento = 'tesoreria.pago-cliente.confirmado.v1' AND deleted_at IS NULL
                      ORDER BY empresa_id, numero_version DESC) v
                WHERE v.lineas::text NOT LIKE '%"Aplicado"%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-CXC06';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-CXC06' AND cuenta_id IS NULL;
                """);
        }
    }
}
