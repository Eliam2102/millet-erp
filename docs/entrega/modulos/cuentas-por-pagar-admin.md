# Cuentas por Pagar — Ficha de administración

## Permisos canónicos (principales)

| Permiso | Rol sugerido |
|---|---|
| `cuentas_por_pagar.facturas.leer` / `.capturar` / `.editar` / `.cancelar` | Auxiliar de CxP |
| `cuentas_por_pagar.facturas.enviar-revision` / `.liberar-revision` | Auxiliar envía; Encargado de área libera |
| `cuentas_por_pagar.facturas.autorizar` | Responsable de CxP (override manual, raro) |
| `cuentas_por_pagar.cfdis.leer` / `.cargar-manual` / `.descartar` | Auxiliar de CxP; `.leer` también al rol de almacén (picker de recepciones) y Comprador |
| `cuentas_por_pagar.anticipos.leer` / `.capturar` | Auxiliar de CxP |
| `cuentas_por_pagar.notas-credito.leer` / `.capturar` | Auxiliar de CxP |
| `cuentas_por_pagar.notas-cargo.leer` / `.crear` / `.aplicar` | Auxiliar de CxP |
| `cuentas_por_pagar.notas-cargo.autorizar` | **Dirección** (compartido con la autorización de devoluciones 8.B) |
| `cuentas_por_pagar.proveedores.poner-revision` / `.liberar-revision` | Responsable de CxP / Encargado de área |
| `cuentas_por_pagar.proveedores.ajustar-tolerancia` | Responsable de CxP (restringido) |
| `cuentas_por_pagar.comprobaciones.*`, `.viaticos.*`, `.tc.*` | Según flujo (aduanales: Comercio Exterior + Dirección de Finanzas) |
| `cuentas_por_pagar.reportes.cartera` / `.antiguedad` / `.diot` / `.tc` | Perfiles de consulta |
| `cuentas_por_pagar.catalogos.aprobadores.administrar`, `.politicas-viaticos.administrar` | RH / Responsable de CxP |

## Configuración previa obligatoria

1. **Tolerancia de conciliación por proveedor** — monto absoluto o porcentaje en
   el master de proveedores; sin configurar, aplica el **default global de
   $0.99 MXN**. Es la tercera tolerancia ortogonal de la triada (las otras dos:
   cantidad por línea en la OC y cantidad por material en Almacén).
2. **Master de proveedores completo** (Datos Maestros): datos fiscales,
   **datos bancarios** (los usa Tesorería al pagar), subcategoría, encargado
   default, días de pago, email para REPP. El alta es conjunta Compras+CxP.
3. **Catálogos locales**: aprobadores con límite (`/cxp/admin/aprobadores`),
   políticas de viáticos (`/cxp/admin/politicas-viaticos`), motivos de revisión,
   categorización de proveedores, tarjetas de crédito. Son prerequisito de los
   flujos internos de gasto (comprobaciones, viáticos, TC) — guion en
   [P7](../pipelines/p7-cxp-gastos-internos.md).
4. **Blob storage de CxP** activo (XML/PDF de CFDIs y evidencias) — verificado
   activo en dev desde 2026-07-15.
5. Serie **FANT** como serie estándar de CFDIs de anticipo (se valida en la
   captura).

## Eventos que publica / consume

| Dirección | Evento | Con quién | Efecto operativo |
|---|---|---|---|
| Publica | `FacturaProveedorRegistradaEvent` | Compras (sub-estado Facturación, acumulados post-evento incluidos), Almacén (concilia variante B) | Factura `Capturada` |
| Publica | `FacturaProveedorRechazadaPorToleranciaEvent` | Compras | Señal de corregir la OC |
| Publica | `FacturaProveedorAutorizadaEvent` / `CanceladaEvent` | Compras, Contabilidad | Estado del pasivo |
| Publica | `PasivoAutorizadoParaPagoEvent` | Tesorería | Pasivo a la bandeja de pagos |
| Publica | `NotaCreditoProveedorRegistradaEvent` | Compras (hoy solo log), Contabilidad | NC general (rel. 01/03/07) |
| Publica | `NotaCreditoFiscalDevolucionRecibidaEvent` | Almacén | NC rel. 03: cierra la devolución 8.B |
| Publica | `CfdiRecibidoIngresadoEvent` | Almacén | Enlace diferido de recepciones variante A |
| Publica | `DiferenciaPrecioFacturaDetectadaEvent` | Almacén (re-valoriza remanente), Compras (informativo) | Diferencia de precio unitario vs OC dentro de tolerancia, variante B (GAP-3 resuelto, #617) |
| Publica | `cuentas_por_pagar.factura.pago-aplicado.v1` | Compras (sub-estado Pago, acumulado por OC incluido) | Al aplicar un pago de Tesorería; habilita el cierre automático de la OC (#613/#614) |
| Consume | `OrdenCompraAutorizadaEvent` / `CanceladaEvent` | `compras-events` / `cuentas-por-pagar-subscription` | Habilita conciliación |
| Consume | `OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent` | `almacen-events` / `cuentas-por-pagar-almacen-sub` | 3-way match; genera NotaCargo |
| Consume | `PagoFacturaProveedorEvent`, `repp-proveedor.recibido.v1` | `tesoreria-events` / `cuentas-por-pagar-tesoreria-sub` | Marca `Pagada`; libera `FALTA_REPP` |

## Monitoreo y troubleshooting

- **Workers en Application Insights**: `ComprasEventListenerWorker`,
  `AlmacenEventListenerWorker` y `TesoreriaEventListenerWorker` (los tres viven
  en CxP) más el `OutboxPublisherWorker` del esquema. Si una factura no pasa a
  `Pagada` tras el pago, revisar el listener de Tesorería y los dead-letters de
  `cuentas-por-pagar-tesoreria-sub`.
- **Dead-letters**: revisar por subscription (arriba); los consumidores son
  idempotentes (correlación por `factura_id`, `recepcion_id`, `nota_cargo_id`),
  reprocesar es seguro.
- **Facturas atoradas en revisión**: SLA de 5 días hábiles con escalamientos
  (día 3, 5, 10); el reporte de cartera cruza categoría × revisión.
- **Rechazos por tolerancia masivos**: si TODA factura gravada rechaza, recordar
  el incidente BUG-1 (la conciliación comparaba el total de OC sin IVA;
  corregido en #611) — hoy un patrón así indicaría regresión.

## Límites conocidos vigentes

- La **NC de proveedor no tiene granularidad por línea de OC**, por lo que
  Compras no puede decrementar `CantidadFacturada` por línea (listener huérfano
  en Compras).
- La API acepta **enums como enteros** (no strings) y fechas **solo en UTC**
  (offset 0) — relevante para integraciones directas; el frontend ya lo maneja.
