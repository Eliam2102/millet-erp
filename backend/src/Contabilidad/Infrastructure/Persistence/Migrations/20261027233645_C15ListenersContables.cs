using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Millet.Contabilidad.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C15ListenersContables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cobros_efectivo_caja",
                schema: "contabilidad",
                columns: table => new
                {
                    cobro_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    caja_sesion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    importe = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cobrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<short>(type: "smallint", nullable: false),
                    poliza_liquidacion_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobros_efectivo_caja", x => x.cobro_id);
                });

            migrationBuilder.CreateTable(
                name: "consumos_contabilizados",
                schema: "contabilidad",
                columns: table => new
                {
                    linea_salida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    salida_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    centro_costo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    poliza_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consumos_contabilizados", x => x.linea_salida_id);
                });

            migrationBuilder.CreateTable(
                name: "eventos_procesados",
                schema: "contabilidad",
                columns: table => new
                {
                    evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evento_tipo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    procesado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eventos_procesados", x => new { x.evento_id, x.evento_tipo });
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobros_efectivo_caja_sesion",
                schema: "contabilidad",
                table: "cobros_efectivo_caja",
                columns: new[] { "empresa_id", "caja_sesion_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_consumos_contabilizados_salida",
                schema: "contabilidad",
                table: "consumos_contabilizados",
                columns: new[] { "empresa_id", "salida_id" });

            migrationBuilder.Sql("""
                -- C1.5: roles nuevos SIN cuenta (Contabilidad los mapea; V12). El rol de comisiones y el de faltantes ya los sembró C1.4.
                INSERT INTO contabilidad.roles_cuenta
                    (id, empresa_id, codigo, descripcion, cuenta_id, sucursal_id, moneda, vigente_desde, vigente_hasta,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, r.codigo, r.descripcion, NULL, NULL, NULL, DATE '2000-01-01', NULL,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C15', 'migration-C15', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('RETENCIONES_CLIENTES', 'Impuestos retenidos por clientes (factura de venta con retenciones)'),
                    ('COBROS_OTRAS_FORMAS', 'Cobros de mostrador con otra forma de pago (ni efectivo, tarjeta, transferencia ni cheque)')
                ) AS r(codigo, descripcion)
                ON CONFLICT DO NOTHING;

                -- C1.5: plantillas #1, #3b, #3c, #4, #5, #7, #8, #9, #11a, #11b, #12, #13, #14, #15a y #15b (plano C1 §4; ficha 86e3kdw33 §3).
                -- Las cancelaciones (#1b, #3, #6, #10, #11c) no tienen plantilla: anulan por documento (K10.3).
                INSERT INTO contabilidad.plantillas_contables
                    (id, empresa_id, tipo_evento, numero_version, vigente_desde, tipo_operacion_clave, concepto, descripcion, lineas,
                     version, created_at, updated_at, created_by, updated_by, deleted_at)
                SELECT gen_random_uuid(), e.id, p.tipo_evento, 1, DATE '2026-01-01', p.clave, p.concepto, p.descripcion, p.lineas::jsonb,
                       1, now() AT TIME ZONE 'UTC', now() AT TIME ZONE 'UTC', 'migration-C15', 'migration-C15', NULL
                FROM compartido.empresas e
                CROSS JOIN (VALUES
                    ('almacen.oc_recepcion.registrada.v1', 'EM', 'Entrada de mercancía', 'Plantilla #1: recepción de OC; inventario contra la cuenta puente por línea de OC (sin activos fijos)', '[{"Orden": 1, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario"}, {"Orden": 2, "Rol": "MERCANCIA_RECIBIDA_POR_FACTURAR", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Mercancía recibida por facturar"}]'),
                    ('almacen.oc_devolucion.registrada.v1', 'EM', 'Devolución a proveedor', 'Plantilla #15b: devolución a proveedor; la cuenta puente contra inventario (la nota de cargo salda el puente)', '[{"Orden": 1, "Rol": "MERCANCIA_RECIBIDA_POR_FACTURAR", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Mercancía devuelta al proveedor"}, {"Orden": 2, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario"}]'),
                    ('almacen.ajuste_inventario.aplicado.v1', 'AS', 'Ajuste de inventario', 'Plantilla #13: faltante a ajuste o merma contra inventario; sobrante al revés', '[{"Orden": 1, "Rol": "AJUSTE_INVENTARIO", "Lado": "Cargo", "Campo": "Faltante", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Faltante de inventario"}, {"Orden": 2, "Rol": "MERMA", "Lado": "Cargo", "Campo": "FaltanteMerma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Merma"}, {"Orden": 3, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "FaltanteTotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario"}, {"Orden": 4, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Sobrante", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario"}, {"Orden": 5, "Rol": "AJUSTE_INVENTARIO", "Lado": "Abono", "Campo": "Sobrante", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Sobrante de inventario"}]'),
                    ('almacen.salida_requisicion.registrada.v1', 'SM', 'Salida de almacén', 'Plantilla #14: consumo por centro de costo contra inventario', '[{"Orden": 1, "Rol": "CONSUMO", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Consumo de almacén"}, {"Orden": 2, "Rol": "INVENTARIO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario"}]'),
                    ('almacen.devolucion_interna.aplicada.v1', 'SM', 'Devolución interna', 'Plantilla #15a: reversa del consumo con la misma cuenta y centro de costo de la salida (V64)', '[{"Orden": 1, "Rol": "INVENTARIO", "Lado": "Cargo", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Inventario"}, {"Orden": 2, "Rol": "CONSUMO", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": true, "Concepto": "Devolución de consumo"}]'),
                    ('cuentas_por_pagar.nota-credito.registrada.v1', 'PC', 'Nota de crédito de proveedor', 'Plantilla #3b: proveedor contra la cuenta puente e IVA acreditable pendiente', '[{"Orden": 1, "Rol": "PROVEEDORES", "Lado": "Cargo", "Campo": "TotalMenosRetenciones", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Proveedor"}, {"Orden": 2, "Rol": "RETENCIONES_POR_PAGAR", "Lado": "Cargo", "Campo": "RetencionesTotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Retenciones por pagar"}, {"Orden": 3, "Rol": "MERCANCIA_RECIBIDA_POR_FACTURAR", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Mercancía recibida por facturar"}, {"Orden": 4, "Rol": "IVA_ACREDITABLE_PENDIENTE", "Lado": "Abono", "Campo": "Iva", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA acreditable pendiente"}]'),
                    ('cuentas_por_pagar.nota-cargo.autorizada.v1', 'PC', 'Nota de cargo a proveedor', 'Plantilla #3c: devolución a proveedor; proveedor contra la cuenta puente e IVA acreditable pendiente', '[{"Orden": 1, "Rol": "PROVEEDORES", "Lado": "Cargo", "Campo": "TotalMenosRetenciones", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Proveedor"}, {"Orden": 2, "Rol": "RETENCIONES_POR_PAGAR", "Lado": "Cargo", "Campo": "RetencionesTotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Retenciones por pagar"}, {"Orden": 3, "Rol": "MERCANCIA_RECIBIDA_POR_FACTURAR", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Mercancía recibida por facturar"}, {"Orden": 4, "Rol": "IVA_ACREDITABLE_PENDIENTE", "Lado": "Abono", "Campo": "Iva", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA acreditable pendiente"}]'),
                    ('cuentas_por_pagar.anticipo.capturado.v1', 'AN', 'Anticipo a proveedor', 'Plantilla #4: anticipo a proveedores contra bancos', '[{"Orden": 1, "Rol": "ANTICIPO_A_PROVEEDORES", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Anticipo a proveedor"}, {"Orden": 2, "Rol": "BANCOS", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos"}]'),
                    ('tesoreria.pago-factura-proveedor.aplicado.v1', 'PP', 'Pago a proveedor', 'Plantilla #5: proveedor contra bancos (la reclasificación de IVA es C1.6)', '[{"Orden": 1, "Rol": "PROVEEDORES", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Proveedor", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Proveedor"}, {"Orden": 2, "Rol": "BANCOS", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos"}]'),
                    ('tesoreria.pago-cliente.confirmado.v1', 'PR', 'Pago de cliente', 'Plantilla #12: bancos contra clientes', '[{"Orden": 1, "Rol": "BANCOS", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Bancos"}, {"Orden": 2, "Rol": "CLIENTES", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}]'),
                    ('facturacion.factura-venta.timbrada.v1', 'RF', 'Factura de venta', 'Plantilla #7: clientes contra ventas por tipo de producto e IVA trasladado no cobrado', '[{"Orden": 1, "Rol": "CLIENTES", "Lado": "Cargo", "Campo": "TotalMenosRetenciones", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}, {"Orden": 2, "Rol": "RETENCIONES_CLIENTES", "Lado": "Cargo", "Campo": "RetencionesTotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Impuestos retenidos por el cliente"}, {"Orden": 3, "Rol": "VENTAS", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Ventas", "RolPorClasificador": true}, {"Orden": 4, "Rol": "IVA_TRASLADADO_NO_COBRADO", "Lado": "Abono", "Campo": "Iva", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA trasladado no cobrado"}]'),
                    ('facturacion.factura-anticipo.timbrada.v1', 'AN', 'Factura de anticipo', 'Plantilla #8: clientes contra anticipos de clientes e IVA trasladado no cobrado', '[{"Orden": 1, "Rol": "CLIENTES", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}, {"Orden": 2, "Rol": "ANTICIPOS_DE_CLIENTES", "Lado": "Abono", "Campo": "Subtotal", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Anticipos de clientes"}, {"Orden": 3, "Rol": "IVA_TRASLADADO_NO_COBRADO", "Lado": "Abono", "Campo": "Iva", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA trasladado no cobrado"}]'),
                    ('facturacion.nota-credito.timbrada.v1', 'RC', 'Nota de crédito a cliente', 'Plantilla #9: descuento sobre ventas (Ranura y demás) o aplicación de anticipo contra clientes', '[{"Orden": 1, "Rol": "DESCUENTOS_SOBRE_VENTAS", "Lado": "Cargo", "Campo": "SubtotalDescuento", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Descuentos sobre ventas"}, {"Orden": 2, "Rol": "IVA_TRASLADADO_NO_COBRADO", "Lado": "Cargo", "Campo": "IvaDescuento", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "IVA trasladado no cobrado"}, {"Orden": 3, "Rol": "CLIENTES", "Lado": "Abono", "Campo": "TotalDescuento", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}, {"Orden": 4, "Rol": "ANTICIPOS_DE_CLIENTES", "Lado": "Cargo", "Campo": "TotalAnticipo", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Aplicación de anticipo"}, {"Orden": 5, "Rol": "CLIENTES", "Lado": "Abono", "Campo": "TotalAnticipo", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}]'),
                    ('facturacion.cobro-mostrador.registrado.v1', 'PR', 'Cobro de mostrador', 'Plantilla #11a: un cobro con tarjeta, transferencia o cheque contra clientes (el efectivo va a la liquidación)', '[{"Orden": 1, "Rol": "COBROS_TC_TD", "Lado": "Cargo", "Campo": "Tarjeta", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cobro con tarjeta"}, {"Orden": 2, "Rol": "BANCOS_MN", "Lado": "Cargo", "Campo": "Transferencia", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cobro por transferencia"}, {"Orden": 3, "Rol": "COBROS_CHEQUE", "Lado": "Cargo", "Campo": "Cheque", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cobro con cheque"}, {"Orden": 4, "Rol": "COBROS_OTRAS_FORMAS", "Lado": "Cargo", "Campo": "OtraForma", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cobro con otra forma de pago"}, {"Orden": 5, "Rol": "CLIENTES", "Lado": "Abono", "Campo": "TotalNoEfectivo", "Auxiliar": "Cliente", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}]'),
                    ('facturacion.caja-sesion.cerrada.v1', 'PR', 'Liquidación de efectivo', 'Plantilla #11b: efectivo en caja de cobranza contra clientes, un abono por cobro (una póliza por corte del cajero)', '[{"Orden": 1, "Rol": "EFECTIVO_CAJA_COBRANZA", "Lado": "Cargo", "Campo": "Total", "Auxiliar": "Ninguno", "PorLinea": false, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Efectivo en caja de cobranza"}, {"Orden": 2, "Rol": "CLIENTES", "Lado": "Abono", "Campo": "Total", "Auxiliar": "Cliente", "PorLinea": true, "SoloServicios": null, "ConCentroCosto": false, "Concepto": "Cliente"}]')
                ) AS p(tipo_evento, clave, concepto, descripcion, lineas)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM contabilidad.plantillas_contables WHERE created_by = 'migration-C15';
                DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (
                    SELECT id FROM contabilidad.roles_cuenta
                    WHERE created_by = 'migration-C15' AND codigo IN ('RETENCIONES_CLIENTES', 'COBROS_OTRAS_FORMAS'));
                DELETE FROM contabilidad.roles_cuenta
                WHERE created_by = 'migration-C15' AND codigo IN ('RETENCIONES_CLIENTES', 'COBROS_OTRAS_FORMAS');
                """);

            migrationBuilder.DropTable(
                name: "cobros_efectivo_caja",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "consumos_contabilizados",
                schema: "contabilidad");

            migrationBuilder.DropTable(
                name: "eventos_procesados",
                schema: "contabilidad");
        }
    }
}
