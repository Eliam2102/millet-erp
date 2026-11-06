using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// PCV (86e3ke10b): plantilla de costo de ventas de la entrega de producto terminado (PT5.6) y sus roles SIN cuenta. Contabilidad los
/// mapea en «Roles de cuenta» (cuentas por operación del 5-nov: costo de venta 501.01.00.00, inventario de producto terminado
/// 115.04.00.00); mientras, cuentas DEMO del sembrador. Solo datos: el modelo no cambia.
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261106090000_PcvPlantillaCostoVentas")]
public sealed class PcvPlantillaCostoVentas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- COSTO_DE_VENTAS es el rol base; con tipo de producto en la línea, el motor usa COSTO_DE_VENTAS_<tipo> si Contabilidad lo mapea.
        INSERT INTO contabilidad.roles_cuenta
            (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-PCV', 'migration-PCV', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('COSTO_DE_VENTAS', 'Costo de venta del producto terminado entregado (sugerida: 501.01.00.00 COSTO DE VENTA)'),
            ('INVENTARIO_PT', 'Inventario de producto terminado (sugerida: 115.04.00.00)')
        ) AS r(codigo, descripcion)
        ON CONFLICT DO NOTHING;

        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-PCV', 'migration-PCV', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('producto_terminado.entrega.confirmada.v1', 'SM', 'Costo de ventas', 'PCV: entrega confirmada de producto terminado; costo de lo entregado contra inventario de PT', '[{"Orden": 1, "Rol": "COSTO_DE_VENTAS", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Costo de ventas", "RolPorClasificador": true}, {"Orden": 2, "Rol": "INVENTARIO_PT", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario de producto terminado"}]')
        ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
        ON CONFLICT DO NOTHING;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-PCV';
        DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (
            SELECT id FROM contabilidad.roles_cuenta WHERE created_by = 'migration-PCV');
        DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-PCV';
        """);
}
