using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// Cuenta por motivo de los movimientos manuales de la app de Almacenes (Fernando Alejos, Contabilidad de Costos, 10-nov-2026). Solo datos:
/// 1) cuenta 115.03.00.00 PRODUCCION EN PROCESO y los roles PRODUCCION_EN_PROCESO (con esa cuenta) e INVENTARIO_EN_PRESTAMO (puente del
///    traspaso entre sucursales; sin cuenta hasta que Contabilidad la defina);
/// 2) plantilla v3 de <c>almacen.salida_inventario.registrada.v1</c> (v2 + muestra → PRODUCCION_EN_PROCESO y préstamo → INVENTARIO_EN_PRESTAMO);
/// 3) plantilla v1 de <c>almacen.entrada_inventario.registrada.v1</c>: cargo inventario (INVENTARIO_PT en devolución de venta) contra MERMA
///    (sobrante, sin compra), COSTO_DE_VENTAS (devolución de venta) o INVENTARIO_EN_PRESTAMO (regreso de préstamo).
/// MERMA y AJUSTE_INVENTARIO ya apuntan a 504.25.02.00 (20261107170000); INVENTARIO_PT y COSTO_DE_VENTAS ya existen como roles (PCV).
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261110120000_AlmCuentasPorMotivo")]
public sealed class AlmCuentasPorMotivo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO contabilidad.cuentas_contables
            (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control,
             no_afectable_manual, codigo_agrupador, grupo_reporte, clase, rubro_id, version, created_at, updated_at, created_by, updated_by)
        SELECT gen_random_uuid(), e.id, '115.03.00.00', 'PRODUCCION EN PROCESO', NULL, 2, 0, 1, 0, 0, false, '115.03', NULL, 0, NULL, 1,
               now(), now(), 'migration-alm-motivo', 'migration-alm-motivo'
        FROM compartido.empresas e
        ON CONFLICT (empresa_id, codigo) DO NOTHING;

        INSERT INTO contabilidad.roles_cuenta
            (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion,
               (SELECT c.id FROM contabilidad.cuentas_contables c WHERE c.empresa_id = e.id AND c.codigo = r.cuenta AND c.deleted_at IS NULL),
               NULL, NULL, DATE '2000-01-01', NULL,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-alm-motivo', 'migration-alm-motivo', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('PRODUCCION_EN_PROCESO', '115.03.00.00', 'Producción en proceso: salida de muestras (Fernando Alejos, 10-nov)'),
            ('INVENTARIO_EN_PRESTAMO', NULL, 'Puente del traspaso por préstamo entre sucursales (inventario contra inventario); falta la cuenta de Contabilidad')
        ) AS r(codigo, cuenta, descripcion)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x
                          WHERE x.empresa_id = e.id AND x.codigo = r.codigo AND x.deleted_at IS NULL AND x.sucursal_id IS NULL AND x.moneda IS NULL);

        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, p.tipo_evento, p.numero_version, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-alm-motivo', 'migration-alm-motivo', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('almacen.salida_inventario.registrada.v1', 3, 'SM', 'Salida de inventario', 'v3: v2 más muestra (producción en proceso) y préstamo entre sucursales (puente de inventario), por motivoClave',
             '[{"Orden": 1, "Rol": "CONSUMO", "Lado": "Cargo", "Campo": "Consumo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Consumo de almacén"}, {"Orden": 2, "Rol": "MERMA", "Lado": "Cargo", "Campo": "Merma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Merma por rotura o daño"}, {"Orden": 3, "Rol": "AJUSTE_INVENTARIO", "Lado": "Cargo", "Campo": "Ajuste", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Salida manual, faltante o no planificada"}, {"Orden": 4, "Rol": "PRODUCCION_EN_PROCESO", "Lado": "Cargo", "Campo": "Muestra", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Muestra a producción en proceso"}, {"Orden": 5, "Rol": "INVENTARIO_EN_PRESTAMO", "Lado": "Cargo", "Campo": "Prestamo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Préstamo a otra sucursal"}, {"Orden": 6, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}]'),
            ('almacen.entrada_inventario.registrada.v1', 1, 'AS', 'Entrada manual de inventario', 'Entrada manual de la app por motivoClave: cargo inventario contra merma (sobrante, sin compra), costo de ventas (devolución de venta) o puente de préstamo (regreso)',
             '[{"Orden": 1, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Inventario", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}, {"Orden": 2, "Rol": "INVENTARIO_PT", "Lado": "Cargo", "Campo": "InventarioPt", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario de producto terminado", "Grupo": "PT"}, {"Orden": 3, "Rol": "MERMA", "Lado": "Abono", "Campo": "Merma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Sobrante o entrada sin compra"}, {"Orden": 4, "Rol": "COSTO_DE_VENTAS", "Lado": "Abono", "Campo": "CostoVentas", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Reversa del costo de ventas por devolución"}, {"Orden": 5, "Rol": "INVENTARIO_EN_PRESTAMO", "Lado": "Abono", "Campo": "Prestamo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Regreso de préstamo entre sucursales"}]')
        ) AS p(tipo_evento, numero_version, clave, concepto, descripcion, lineas)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x
                          WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento AND x.numero_version = p.numero_version);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-alm-motivo';
        DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-alm-motivo';
        """);
}
