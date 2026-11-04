using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// F1-ALM-13 (Contabilidad, Fernando Alejos 4-nov): una cuenta de inventario por tipo de artículo para toda la empresa.
    /// <list type="bullet">
    ///   <item>Plantillas v2 de Almacén (#1, #13, #14, #15a, #15b): la partida de INVENTARIO va por línea con rol por clasificador
    ///   (INVENTARIO_{grupo del artículo} si tiene cuenta; si no, INVENTARIO). La v1 queda para la historia.</item>
    ///   <item>Roles INVENTARIO_{grupo} solo para los grupos SAP sin ambigüedad, con la cuenta del catálogo de Fernando si ya existe
    ///   en la empresa (si no, quedan sin cuenta y Contabilidad la asigna en Roles de cuenta). 115.05 (tránsito) no se siembra.</item>
    /// </list>
    /// </summary>
    public partial class Alm13InventarioPorGrupo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion,
                       (SELECT c.id FROM contabilidad.cuentas_contables c
                        WHERE c.empresa_id = e.id AND c.codigo = r.cuenta AND c.deleted_at IS NULL LIMIT 1),
                       NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-ALM13', 'migration-ALM13', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('INVENTARIO_VIDRIO_FLOTADO', 'Inventario del grupo VIDRIO FLOTADO (115.02.01.00 MATERIA PRIMA - VIDRIO)', '115.02.01.00'),
                    ('INVENTARIO_VIDRIO_TEMPLADO', 'Inventario del grupo VIDRIO TEMPLADO (115.02.01.00 MATERIA PRIMA - VIDRIO)', '115.02.01.00'),
                    ('INVENTARIO_VIDRIO_LAMINADO', 'Inventario del grupo VIDRIO LAMINADO (115.02.02.00 MATERIA PRIMA - LAMINADO/INSULADO)', '115.02.02.00'),
                    ('INVENTARIO_VIDRIO_INSULADO', 'Inventario del grupo VIDRIO INSULADO (115.02.02.00 MATERIA PRIMA - LAMINADO/INSULADO)', '115.02.02.00'),
                    ('INVENTARIO_PINT_ELEC_QUIM_MP', 'Inventario del grupo PINT.ELEC.QUÍM. (MP) (115.02.04.00 MP PINTURA ELECTROSTATICA)', '115.02.04.00'),
                    ('INVENTARIO_HERRAJES_IMPORTACION', 'Inventario del grupo HERRAJES IMPORTACIÓN (115.07.01.00 HERRAJES)', '115.07.01.00'),
                    ('INVENTARIO_HERRAJES_NACIONALES', 'Inventario del grupo HERRAJES NACIONALES (115.07.01.00 HERRAJES)', '115.07.01.00'),
                    ('INVENTARIO_ACCESORIOS', 'Inventario del grupo ACCESORIOS (115.07.02.00 ACCESORIOS)', '115.07.02.00')
                ) AS r(codigo, descripcion, cuenta)
                ON CONFLICT DO NOTHING;

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, p.tipo_evento, 2, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-ALM13', 'migration-ALM13', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('almacen.oc_recepcion.registrada.v1', 'EM', 'Entrada de mercancía', 'Plantilla #1 v2: recepción de OC; inventario por grupo de artículo contra la cuenta puente por línea de OC (sin activos fijos)', '[{"Orden": 1, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}, {"Orden": 2, "Rol": "MERCANCIA_RECIBIDA_POR_FACTURAR", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Mercancía recibida por facturar"}]'),
                    ('almacen.oc_devolucion.registrada.v1', 'EM', 'Devolución a proveedor', 'Plantilla #15b v2: devolución a proveedor; la cuenta puente contra inventario por grupo de artículo (la nota de cargo salda el puente)', '[{"Orden": 1, "Rol": "MERCANCIA_RECIBIDA_POR_FACTURAR", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Mercancía devuelta al proveedor"}, {"Orden": 2, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}]'),
                    ('almacen.ajuste_inventario.aplicado.v1', 'AS', 'Ajuste de inventario', 'Plantilla #13 v2: faltante a ajuste o merma contra inventario por grupo de artículo; sobrante al revés', '[{"Orden": 1, "Rol": "AJUSTE_INVENTARIO", "Lado": "Cargo", "Campo": "Faltante", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Faltante de inventario"}, {"Orden": 2, "Rol": "MERMA", "Lado": "Cargo", "Campo": "FaltanteMerma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Merma"}, {"Orden": 3, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Neto", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}, {"Orden": 4, "Rol": "AJUSTE_INVENTARIO", "Lado": "Abono", "Campo": "Sobrante", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Sobrante de inventario"}]'),
                    ('almacen.salida_requisicion.registrada.v1', 'SM', 'Salida de almacén', 'Plantilla #14 v2: consumo por centro de costo contra inventario por grupo de artículo', '[{"Orden": 1, "Rol": "CONSUMO", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Consumo de almacén"}, {"Orden": 2, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}]'),
                    ('almacen.devolucion_interna.aplicada.v1', 'SM', 'Devolución interna', 'Plantilla #15a v2: inventario por grupo de artículo contra la misma cuenta y centro de costo de la salida (V64)', '[{"Orden": 1, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}, {"Orden": 2, "Rol": "CONSUMO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Devolución de consumo"}]')
                ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-ALM13';
                DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (
                    SELECT id FROM contabilidad.roles_cuenta WHERE created_by = 'migration-ALM13');
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-ALM13';
                """);
        }
    }
}
