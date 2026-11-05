using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261105190000_P19PolizasComercioExterior")]
public sealed class P19PolizasComercioExterior : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Sin números inventados: Contabilidad asigna las cuentas en la configuración de roles.
        migrationBuilder.Sql("""
            INSERT INTO contabilidad.roles_cuenta
                (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                 version, created_at, updated_at, created_by, updated_by, deleted_at)
            SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                   1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-P19', 'migration-P19', NULL
            FROM compartido.empresas e
            CROSS JOIN (VALUES
            ('ANTICIPO_AGENTE_ADUANAL', 'Anticipo al agente aduanal; configure la cuenta antes de contabilizar'),
            ('GASTO_IMPORTACION', 'Gasto de importación'),
            ('GASTOS_IMPORTACION_POR_APLICAR', 'Gastos de importación por aplicar')
            ) AS r(codigo, descripcion)
            WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);
            INSERT INTO contabilidad.plantillas_contables
                (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                 version, created_at, updated_at, created_by, updated_by, deleted_at)
            SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2000-01-01', p.clave, p.concepto,
                   p.concepto, p.lineas::jsonb, 1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-P19', 'migration-P19', NULL
            FROM compartido.empresas e
            CROSS JOIN (VALUES
            ('almacen.expediente.costo-aplicado.v1', 'AS', 'Prorrateo de gastos de importación', '[{"Orden": 1, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Inventario", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}, {"Orden": 2, "Rol": "GASTO_IMPORTACION", "Lado": "Cargo", "Campo": "GastoImportacion", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Gasto importacion", "RolPorClasificador": false}, {"Orden": 3, "Rol": "GASTOS_IMPORTACION_POR_APLICAR", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Gastos importacion por aplicar", "RolPorClasificador": false}]'),
            ('tesoreria.anticipo-agente.pagado.v1', 'AN', 'Pago de anticipo al agente aduanal', '[{"Orden": 1, "Rol": "ANTICIPO_AGENTE_ADUANAL", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Anticipo agente aduanal", "RolPorClasificador": false}, {"Orden": 2, "Rol": "BANCOS", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos", "RolPorClasificador": false}]'),
            ('tesoreria.anticipo-agente.devolucion-confirmada.v1', 'AS', 'Devolución de anticipo del agente aduanal', '[{"Orden": 1, "Rol": "BANCOS", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos", "RolPorClasificador": false}, {"Orden": 2, "Rol": "ANTICIPO_AGENTE_ADUANAL", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Anticipo agente aduanal", "RolPorClasificador": false}]')
            ) AS p(tipo_evento, clave, concepto, lineas)
            WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-P19';
        DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-P19' AND cuenta_id IS NULL;
        """);
}
