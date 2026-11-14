using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations;

/// <summary>
/// Venta por albarán (VENTA_ALBARAN, contrato v10-v11): plantilla v4 de <c>almacen.salida_inventario.registrada.v1</c> = la v3 más el
/// cargo COSTO_DE_VENTAS del campo <c>CostoVentas</c>. Solo datos; el motor toma la versión más alta vigente.
/// <para>
/// Por qué: la salida de materia prima por una venta de A+W no se contabilizaba en ninguna parte. La factura del pedido
/// (<c>facturacion.factura-venta.timbrada.v1</c>) solo registra el ingreso, y el costo de PT abona INVENTARIO_PT, que es otra cuenta y
/// solo cubre las piezas de producto terminado. Sin este cargo, el inventario de materia prima bajaría sin contrapartida.
/// El rol COSTO_DE_VENTAS ya existe (PCV) y la entrada por devolución de venta ya lo abona como reversa
/// (<c>20261110120000_AlmCuentasPorMotivo</c>), así que la venta y su devolución quedan simétricas.
/// </para>
/// </summary>
[DbContext(typeof(ContabilidadDbContext))]
[Migration("20261113230000_VentaAlbaranCostoVentas")]
public sealed class VentaAlbaranCostoVentas : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO contabilidad.plantillas_contables
            (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
             version, created_at, updated_at, created_by, updated_by, deleted_at)
        SELECT gen_random_uuid(), e.id, p.tipo_evento, p.numero_version, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
               1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-venta-albaran', 'migration-venta-albaran', NULL
        FROM compartido.empresas e
        CROSS JOIN (VALUES
            ('almacen.salida_inventario.registrada.v1', 4, 'SM', 'Salida de inventario', 'v4: v3 más la venta por albarán (costo de ventas)',
             '[{"Orden": 1, "Rol": "CONSUMO", "Lado": "Cargo", "Campo": "Consumo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Consumo de almacén"}, {"Orden": 2, "Rol": "MERMA", "Lado": "Cargo", "Campo": "Merma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Merma por rotura o daño"}, {"Orden": 3, "Rol": "AJUSTE_INVENTARIO", "Lado": "Cargo", "Campo": "Ajuste", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Salida manual, faltante o no planificada"}, {"Orden": 4, "Rol": "PRODUCCION_EN_PROCESO", "Lado": "Cargo", "Campo": "Muestra", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Muestra a producción en proceso"}, {"Orden": 5, "Rol": "INVENTARIO_EN_PRESTAMO", "Lado": "Cargo", "Campo": "Prestamo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Préstamo a otra sucursal"}, {"Orden": 6, "Rol": "COSTO_DE_VENTAS", "Lado": "Cargo", "Campo": "CostoVentas", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Costo de ventas de la venta por albarán"}, {"Orden": 7, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}]')
        ) AS p(tipo_evento, numero_version, clave, concepto, descripcion, lineas)
        WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x
                          WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento AND x.numero_version = p.numero_version);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-venta-albaran';
        """);
}
