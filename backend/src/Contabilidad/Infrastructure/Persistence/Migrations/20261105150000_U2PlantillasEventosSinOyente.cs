using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class U2PlantillasEventosSinOyente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // U-2 (86e3yxngq): plantillas de los eventos que ya se publicaban y Contabilidad no recibía (Tesorería, costo de importación y
            // salidas G-1). Los roles nuevos nacen SIN cuenta (Contabilidad los mapea; sin cuenta el motor deja el evento en la bandeja).
            // El rol del concepto «Comisión bancaria» toma la cuenta que ya tenga COMISIONES_BANCARIAS (la misma comisión del cobro corto).
            migrationBuilder.Sql("""
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion,
                       CASE WHEN r.codigo = 'CONCEPTO_BANCARIO_COMISION_BANCARIA'
                            THEN (SELECT c.cuenta_id FROM contabilidad.roles_cuenta c
                                  WHERE c.empresa_id = e.id AND c.codigo = 'COMISIONES_BANCARIAS' AND c.sucursal_id IS NULL
                                    AND c.moneda IS NULL AND c.deleted_at IS NULL
                                  ORDER BY c.vigente_desde DESC LIMIT 1)
                       END,
                       NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-U2', 'migration-U2', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('CONCEPTO_BANCARIO', 'Concepto bancario sin rol propio (U-2): la contrapartida de un alta desde el extracto'),
                    ('CONCEPTO_BANCARIO_COMISION_BANCARIA', 'Concepto bancario «Comisión bancaria» (U-2)'),
                    ('CONCEPTO_BANCARIO_INTERESES_GANADOS', 'Concepto bancario «Intereses ganados» (U-2)'),
                    ('CONCEPTO_BANCARIO_INTERESES_Y_GASTOS_FINANCIEROS', 'Concepto bancario «Intereses y gastos financieros» (U-2)'),
                    ('GASTOS_IMPORTACION_POR_APLICAR', 'Gastos de importación por aplicar (U-2): se abona al repartir el costo del expediente'),
                    ('COSTO_IMPORTACION_A_RESULTADOS', 'Costo de importación de piezas que ya no están en existencia (U-2, Opción A del 26-oct)')
                ) AS r(codigo, descripcion)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.roles_cuenta x WHERE x.empresa_id = e.id AND x.codigo = r.codigo);

                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-U2', 'migration-U2', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('tesoreria.deposito-caja.confirmado.v1', 'DP', 'Depósito del corte de caja', 'U-2: ficha de depósito del corte de efectivo; bancos contra efectivo en caja de cobranza', '[{"Orden": 1, "Rol": "BANCOS", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos"}, {"Orden": 2, "Rol": "EFECTIVO_CAJA_COBRANZA", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Efectivo en caja de cobranza"}]'),
                    ('tesoreria.diferencia-corte.autorizada.v1', 'AS', 'Diferencia de corte de caja', 'U-2: faltante a deudores funcionarios y empleados (cajero) contra efectivo en caja; el sobrante al revés', '[{"Orden": 1, "Rol": "DEUDORES_FUNCIONARIOS_EMPLEADOS", "Lado": "Cargo", "Campo": "Neto", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Diferencia de corte del cajero"}, {"Orden": 2, "Rol": "EFECTIVO_CAJA_COBRANZA", "Lado": "Abono", "Campo": "Neto", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Efectivo en caja de cobranza"}]'),
                    ('tesoreria.diferencia-corte.devolucion-aplicada.v1', 'AS', 'Devolución de faltante de corte', 'U-2: el cajero devuelve el faltante; efectivo en caja contra deudores funcionarios y empleados', '[{"Orden": 1, "Rol": "EFECTIVO_CAJA_COBRANZA", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Efectivo en caja de cobranza"}, {"Orden": 2, "Rol": "DEUDORES_FUNCIONARIOS_EMPLEADOS", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Devolución de faltante del cajero"}]'),
                    ('tesoreria.movimiento-bancario.registrado.v1', 'AS', 'Movimiento bancario del extracto', 'U-2: alta desde el extracto; concepto bancario (rol por concepto) e IVA acreditable contra bancos; un ingreso invierte los lados', '[{"Orden": 1, "Rol": "CONCEPTO_BANCARIO", "Lado": "Cargo", "Campo": "Base", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Concepto bancario", "RolPorClasificador": true}, {"Orden": 2, "Rol": "IVA_ACREDITABLE_PAGADO", "Lado": "Cargo", "Campo": "Iva", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA acreditable"}, {"Orden": 3, "Rol": "BANCOS", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos"}]'),
                    ('tesoreria.reembolso-nota-credito.actualizado.v1', 'AS', 'Reembolso a cliente por nota de crédito', 'U-2: devolución de dinero al cliente al pagarse; clientes contra bancos', '[{"Orden": 1, "Rol": "CLIENTES", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}, {"Orden": 2, "Rol": "BANCOS", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos"}]'),
                    ('almacen.expediente.costo-aplicado.v1', 'AS', 'Costo de importación aplicado', 'U-2: inventario por grupo de artículo y la parte que no quedó en existencia contra gastos de importación por aplicar', '[{"Orden": 1, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Inventario", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Costo de importación al inventario", "RolPorClasificador": true}, {"Orden": 2, "Rol": "COSTO_IMPORTACION_A_RESULTADOS", "Lado": "Cargo", "Campo": "Gasto", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Costo de importación de piezas sin existencia"}, {"Orden": 3, "Rol": "GASTOS_IMPORTACION_POR_APLICAR", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Gastos de importación por aplicar"}]'),
                    ('almacen.salida_inventario.registrada.v1', 'SM', 'Salida de inventario', 'U-2 (G-1): consumo, merma o ajuste según la causa contra inventario por grupo de artículo', '[{"Orden": 1, "Rol": "CONSUMO", "Lado": "Cargo", "Campo": "Consumo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Consumo de almacén"}, {"Orden": 2, "Rol": "MERMA", "Lado": "Cargo", "Campo": "Merma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Merma por rotura o daño"}, {"Orden": 3, "Rol": "AJUSTE_INVENTARIO", "Lado": "Cargo", "Campo": "Ajuste", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Salida manual o no planificada"}, {"Orden": 4, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario", "RolPorClasificador": true}]')
                ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
                WHERE NOT EXISTS (SELECT 1 FROM contabilidad.plantillas_contables x WHERE x.empresa_id = e.id AND x.tipo_evento = p.tipo_evento);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-U2';
                DELETE FROM contabilidad.roles_cuenta WHERE created_by = 'migration-U2' AND (cuenta_id IS NULL OR codigo = 'CONCEPTO_BANCARIO_COMISION_BANCARIA');
                """);
        }
    }
}
