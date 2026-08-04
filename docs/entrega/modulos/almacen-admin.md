# Almacén — Ficha de administración

## Permisos canónicos (principales)

| Permiso | Rol sugerido |
|---|---|
| `almacen.entradas.leer` / `.capturar` / `.registrar` / `.cancelar-borrador` | Supervisor de Insumos, Almacenistas |
| `almacen.salidas.leer-propias` / `.leer-todas` / `.capturar` / `.registrar` | Almacenistas (leer-todas: supervisión) |
| `almacen.salidas.por-vale` | Almacenistas autorizados para urgencias |
| `almacen.devoluciones-internas.leer` / `.capturar` | Almacenistas / Supervisor |
| `almacen.devoluciones-proveedor.iniciar` / `.autorizar` / `.registrar` | Jefe de Almacén inicia; **Dirección autoriza** (compartido con `cuentas_por_pagar.notas-cargo.autorizar`); Almacenista registra |
| `almacen.inventarios.leer` / `.crear` / `.capturar` / `.aprobar-nivel1/2/3` | Contadores capturan; aprobación N1 <$1K Almacenista, N2 $1K–$10K Supervisor, N3 >$10K Jefe de Almacén + Finanzas |
| `almacen.ajustes.manual` | Restringido, auditable |
| `almacen.asignaciones.leer` / `.administrar` | Administrador del almacén |
| `almacen.ubicaciones.*`, `almacen.almacenes.*`, `almacen.reorden.*` | Administrador del almacén |
| `almacen.cierre-mes.ejecutar` | Jefe de Almacén |
| `almacen.reportes.alfak` / `.mp-cnk` / `.movimientos`, `almacen.lectura.total` | Reportes; lectura total para Auditor Externo |
| `cuentas_por_pagar.cfdis.leer` | **Necesario para el rol de almacén de insumos**: el picker de CFDIs de la recepción variante A lo consume |

## Configuración previa obligatoria

1. **Asignación artículo→ubicación** (`/almacen/asignaciones`) para cada
   artículo que se vaya a recibir en cada ubicación. **Sin ella la recepción
   falla con `ENTRADA_SIN_ASIGNACION`** — es el prerequisito que más operaciones
   bloqueó en la verificación. La ubicación ÚNICA (default) no admite entradas
   ni asignaciones (`ASIGNACION_A_UNICA`).
2. **Estructura física completa**: Sucursal → Almacén → Sub-almacén →
   Ubicaciones N4 (racks/pasillos), en `/almacen/almacenes`, `/almacen/sub-almacenes`,
   `/almacen/ubicaciones`.
3. **Tolerancia de cantidad por material** (catálogo de Datos Maestros) para
   recepciones con sobre/sub-cantidad; la de **cantidad por línea** viene de la
   OC y la de **monto** es del proveedor (CxP) — tres tolerancias ortogonales.
4. **Umbrales monetarios de aprobación de ajustes** (N1/N2/N3).
5. Sub-almacén de tipo **Material en revisión** para devoluciones de dañado.
6. Permiso `cuentas_por_pagar.cfdis.leer` otorgado al rol de almacén
   (⚠️ pendiente formalizar en el alta de roles — serie #575-#577).
7. **Motor de reabasto** (si se usa): doble interruptor con precedencia —
   `ReordenWorker:Disabled` (app setting en Bicep, default **deshabilitado**,
   opt-in por ambiente con reinicio) y el toggle operativo
   `ReabastoAutomaticoActivo` en la pantalla `/almacen/reorden`; además el
   **usuario de servicio** del motor debe existir (sin él,
   `REORDEN_SP_NO_DISPONIBLE`). Guion en
   [P8](../pipelines/p8-almacen-reabasto-cierre.md).
8. **Cierre de mes**: proceso mensual del Jefe de Almacén; exige el mes limpio
   (conteos aplicados/rechazados, movimientos firmados/cancelados) — ver
   [P8](../pipelines/p8-almacen-reabasto-cierre.md).

## Eventos que publica / consume

| Dirección | Evento | Con quién | Efecto operativo |
|---|---|---|---|
| Publica | `OcRecepcionRegistradaEvent` (flag `factura_pendiente` en variante B) | Compras (sub-estado Recepción), CxP (conciliación 3-way) | Cada entrada firme contra OC |
| Publica | `OcDevolucionRegistradaEvent` (con `LineaOcId`, #615) | Compras (decrementa `CantidadRecibida`, reabre la OC si estaba `Cerrada` — #614), CxP (genera NotaCargo) | Devolución 8.B registrada |
| Consume | `OrdenCompraAutorizadaEvent` / `CanceladaEvent` / `CerradaEvent` | topic `compras-events` | Habilita/cierra la OC para recepción |
| Consume | `FacturaProveedorRegistradaEvent` | topic `cuentas-por-pagar-events`, subscription `almacen-subscription` | Variante B: concilia la recepción pendiente |
| Consume | `NotaCreditoFiscalDevolucionRecibidaEvent` | mismo topic | Cierra la devolución 8.B como `ConciliadaConNcFiscal` |
| Consume | `CfdiRecibidoIngresadoEvent` | mismo topic | Enlace diferido de recepciones variante A capturadas con UUID a mano |
| Consume | `DiferenciaPrecioFacturaDetectadaEvent` | mismo topic | Movimiento `AjustePrecioFactura`: re-valoriza la cantidad remanente en stock y avisa a Contabilidad (GAP-3 resuelto, #617) |

## Monitoreo y troubleshooting

- **Dead-letters en `almacen-subscription`** (topic `cuentas-por-pagar-events`):
  primer lugar a revisar si una recepción variante B no concilia o una
  devolución no llega a `ConciliadaConNcFiscal`. Incidente conocido (corregido
  en #612): el filtro de la subscription no incluía
  `cuentas_por_pagar.nota-credito.registrada.v1` y las NC fiscales jamás
  llegaban; quedaron 2-3 dead-letters históricos de esa época en dev.
- **`CxpEventListenerWorker` (en Almacén)** en Application Insights: consume el
  topic de CxP; verificar que procesa tras cada deploy.
- **Recepción "fantasma" sin OC**: va a la bandeja de pendientes sin afectar
  inventario — no es error.
- **Conteo anual atorado**: bloquea salidas del sub-almacén hasta aplicarse o
  rechazarse; si la operación reclama salidas bloqueadas, revisar
  `/almacen/inventarios` por conteos anuales en curso.

## Límites conocidos vigentes

- Menor: la bandera `factura_pendiente` de la proyección local
  (`recepciones_oc_local`) no se apaga al conciliar la variante B — cosmético,
  no afecta saldos.
