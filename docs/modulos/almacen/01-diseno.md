# Diseño — Módulo Almacén (`Millet.Almacen`)

> **Construido sobre:** [00-levantamiento.md](00-levantamiento.md) (Rev. 0.1.1, 2026-05-22).
>
> **Hereda contexto de:**
> - [`docs/modulos/compras-ordenes-compra/01-diseno.md`](../compras-ordenes-compra/01-diseno.md) — define el contrato `OcRecepcionRegistradaEvent` que Almacén debe satisfacer.
> - [`docs/modulos/cuentas-por-pagar/00-levantamiento.md`](../cuentas-por-pagar/00-levantamiento.md) §11.6 — contratos cross-módulo CxP ↔ Almacén canónicos.
> - [`docs/modulos/administracion/01-diseno.md`](../administracion/01-diseno.md) — dueño del placeholder `Almacen` (MVP-light en `DatosMaestros`) que este módulo re-localizará.
> - [ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md) — estrategia de reportería (motor nativo).
>
> **Estado:** propuesta de diseño v1 para revisión con el owner. Las decisiones marcadas como `[Asunción Axx]` requieren confirmación antes de implementar; están listadas en §3.
>
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer este documento

- `[Decidido]` — fijado por ADR existente, mapa funcional §12.1 cerrado, o por sesión con Carlos.
- `[Asunción Axx]` — propuesta del diseñador. Pendiente de confirmación. Listadas en §3.
- `[Diferido]` — fuera de alcance v1; anotado para no perderlo.
- `[Pendiente]` — decisión existe pero se cierra antes del 02-plan o antes de implementar.

Este documento describe **qué construir y por qué**, no el código. La implementación sigue las convenciones del repo (hexagonal, CQRS con MediatR, EF Core, FluentValidation, Mapster, Serilog, records, sealed por defecto, nullable reference types).

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

- **Módulo:** `Almacen`. Módulo de back-office independiente.
- **Esquema PostgreSQL:** `almacen` (ADR-0030 — un esquema por módulo).
- **Bounded context:** `Almacen`. Dueño del inventario físico, movimientos, conteos.
- **Proyecto .NET:** `backend/src/Almacen/` (nuevo). Namespaces: `Millet.Almacen.Domain`, `Millet.Almacen.Application`, `Millet.Almacen.Infrastructure`, `Millet.Api.Endpoints.Almacen`.
- **DbContext:** `AlmacenDbContext` (ADR-0030). Se registra en `MigrationsHealthCheckOptions.ContextTypes` y en `deploy-app-dev.yml` (memoria `feedback_dbcontext_nuevo_checklist`).
- **Re-localización del placeholder de Administración:** la entidad `Almacen` actual en `DatosMaestros` (MVP-light, schema `compartido`) se re-localiza a este módulo en F1-PR1. Migración aditiva: tablas duplicadas temporalmente con copy-from, luego `DROP` del placeholder.

### 1.2 Alcance funcional v1 (MVP)

**Dentro:**

1. **Catálogo de Almacenes y Sub-almacenes** (3 niveles jerárquicos: Sucursal → Almacén → Sub-almacén). Sin bins en MVP (texto libre opcional en línea de movimiento — §3.1 del levantamiento).
2. **Submódulo Entradas — Variante A (con factura)** para insumos/refacciones.
3. **Submódulo Entradas — Variante B (con packing list)** para materiales directos no-vidrio (interlayer, silicones, pinturas).
4. **Submódulo Salidas — Variante A (normal)** con RQ aprobada.
5. **Submódulo Salidas — Variante B (vale urgente)** con regularización en 48h.
6. **Submódulo Inventario Físico** — rotativo (sin bloqueo) y anual (con bloqueo). Captura sin sesgo, recuento por umbral, aprobación por monto.
7. **Submódulo Devoluciones — Sub-flujo 8.A (interna)** — solicitante regresa material al almacén.
8. **Submódulo Devoluciones — Sub-flujo 8.B (a proveedor)** — Almacén envía mercancía recibida al proveedor; cruza con CxP.
9. **Submódulo Reportes** — ALFAK-HISTORIAL-ALMACEN (cierre de mes) + SAP-REPORTE-EXISTENCIA-MP-CNK (inventario diario MP). Motor nativo (ADR-0036).
10. **Eventos de integración** — publicar `OcRecepcionRegistradaEvent`, `OcDevolucionRegistradaEvent`, `SalidaRequisicionRegistradaEvent`, `DevolucionInternaAplicadaEvent`, `AjusteInventarioAplicadoEvent`, `EntradaInventarioValoradaEvent`. Suscribir `OrdenCompraAutorizadaEvent`, `OrdenCompraCanceladaEvent`, `OrdenCompraCerradaEvent`, `FacturaProveedorRegistradaEvent` (variante B), `DiferenciaPrecioFacturaDetectadaEvent`, `NotaCreditoFiscalDevolucionRecibidaEvent`, `RequisicionAprobadaEvent`, `RequisicionCanceladaEvent`.
11. **Workers en-proceso** — `OutboxPublisherWorker<AlmacenDbContext>`, `RegularizacionValeSlaWorker`, `InventarioRotativoCalendarioWorker`.
12. **RBAC granular** — permisos canónicos `almacen.*` (ADR-0007).
13. **Auditoría e inmutabilidad de movimientos** (§10.1 del levantamiento, ADR-0008, ADR-0012).
14. **Reservas de stock** (decisión 2026-05-22, opción B del teardown de Compras) — agregado `ReservaStock` + columna `cantidad_reservada` en `saldos_inventario` + comandos `ReservarStockCommand` / `LiberarReservaCommand` + eventos `StockReservadoEvent` / `StockLiberadoEvent` + puerto público `IAlmacenReservaPort` consumido por Requisiciones de Compras al transmitir RQ.

**Fuera (cerrado en §12.2 del levantamiento):**

- **Operación móvil con códigos de barras y escáner.** `[Diferido]` vNext.
- **Gestión formal de bins/ubicaciones físicas.** `[Diferido]` vNext.
- **Clasificación ABC + planificación automática de conteos cíclicos.** `[Diferido]`.
- **Putaway y picking dirigidos.** `[Diferido]`.
- **Traspasos internos entre sub-almacenes.** `[Pendiente]` confirmar si MVP o vNext. Por defecto **vNext**.
- ~~**Reservas de stock.**~~ **Movido a MVP** (decisión 2026-05-22) — ver punto 14 del alcance dentro.
- **Vidrio crudo.** Sigue en A+W (fuera del ERP).
- **Reportes 3–6 del Portal Millet.** `[Diferido]`.

### 1.3 Volúmenes esperados

Del §8 del levantamiento original (consolidado en §11.3):

- Insumos: ~40 facturas/día entrada (~1,200/mes), ~120 salidas/día (~3,600/mes).
- Materiales directos: ~7 facturas/semana entrada (~30/mes), ~3 salidas/día (~90/mes).
- Pico viernes-lunes + fin de mes.
- 3 usuarios concurrentes mínimos (2 licencias permanentes + 1 compartida).
- Operación a 2 turnos (8am-6pm + 11pm-8am).

**Implicación de diseño:** sin necesidades de escala especiales para v1. Un solo nodo .NET. Tablas de movimientos crecen ~5K filas/mes; sin particionamiento en v1. Bandejas con ≤500 filas activas se cubren con índices del §5.1; los reportes históricos usan rangos de fecha.

---

## 2. Decisiones de diseño y ADRs aplicados

Hereda los ADRs transversales. La tabla resume los aplicados.

| Tema | Decisión | Referencia |
|---|---|---|
| Identidad y autorización | Entra ID + RBAC granular | [ADR-0003](../../decisiones/0003-autenticacion-entra-id.md), [ADR-0007](../../decisiones/0007-autorizacion-rbac-granular.md) |
| Concurrencia | Optimista por `Version` (Capa 1) + Soft lock SignalR (Capa 2) | [ADR-0012](../../decisiones/0012-concurrencia-hibrida.md) |
| Auditoría | Framework centralizado | [ADR-0008](../../decisiones/0008-estrategia-auditoria.md) |
| Migraciones | EF Core, esquema por módulo | [ADR-0005](../../decisiones/0005-migraciones-ef-core-esquema-por-modulo.md) |
| Multi-DbContext | Cada módulo con su DbContext + esquema | [ADR-0030](../../decisiones/0030-multi-dbcontext-por-modulo.md) |
| Validación | FluentValidation por comando | [ADR-0018](../../decisiones/0018-validacion-fluentvalidation.md) |
| Eventos de integración | Outbox + Service Bus | [ADR-0009](../../decisiones/0009-outbox-pattern-eventos-integracion.md) |
| Errores HTTP | Problem Details (RFC 7807) | [ADR-0010](../../decisiones/0010-manejo-errores-problem-details.md) |
| Versionado API | URL versioned (`/api/v1/...`) | [ADR-0021](../../decisiones/0021-versionado-api-rest.md) |
| Idempotencia HTTP | Header `Idempotency-Key` en POST | [ADR-0020](../../decisiones/0020-idempotencia-http.md) |
| Notificaciones email | Vía módulo Notificaciones | [ADR-0026](../../decisiones/0026-notificaciones-email.md) |
| Multi-empresa | `EmpresaId` en agregados | [ADR-0011](../../decisiones/0011-multi-empresa-empresa-id.md) |
| Tiempo / zonas | UTC en BD | [ADR-0013](../../decisiones/0013-tiempo-zona-horaria.md) |
| Multimoneda | `Money` VO; T/C al día del movimiento | [ADR-0014](../../decisiones/0014-money-multimoneda-tipos-de-cambio.md) |
| Adjuntos | Blob storage | [ADR-0024](../../decisiones/0024-almacenamiento-documentos.md) |
| Generación PDF | Servicio compartido (recompro de salida) | [ADR-0025](../../decisiones/0025-generacion-pdfs.md) |
| Background jobs | `IHostedService` dentro de `Millet.Api` | [ADR-0022](../../decisiones/0022-background-jobs.md) |
| Stubs cross-module | PLATFORM-TODO | [ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md) |
| Reportería | Motor nativo React + JSON + `@react-pdf/renderer` + `exceljs` | [ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md) |

---

## 3. Asunciones que deben confirmarse

| # | Asunción | Default propuesto | Si el cliente dice "no" |
|---|---|---|---|
| A1 | **Volumetría real** | ~5K movimientos/mes. Sin particionamiento en v1. | Si supera 50K/mes, evaluar particionamiento por mes en `movimientos_inventario`. |
| A2 | **Folio de movimiento** | `M-{tipo_prefijo}{año}-{secuencial:6}`. Ej: `M-ENT2026-000001`, `M-SAL2026-000001`, `M-AJP2026-000001`, `M-DEV2026-000001`. Secuencia atómica PostgreSQL por (tipo, año). | Si el área quiere preservar nomenclatura SAP, se ajusta. |
| A3 | **Costo promedio ponderado** como método de valoración | Estándar industria. Confirmar con Finanzas que SAP actual usa el mismo método antes de migrar saldos. | Si SAP usa FIFO o último costo, se cambia el algoritmo (impacta la tabla `costos_inventario`). |
| A4 | **Saldo de inventario calculado vs materializado** | **Materializado** en tabla `saldos_inventario` actualizada por cada movimiento (consistencia eventual no — actualización en la misma transacción del movimiento). Razón: bandejas de stock requieren acceso O(1); calcular por agregación es prohibitivo con 5K movimientos/mes. | Si el equipo prefiere derivar siempre, se elimina la tabla y se materializa en vista. |
| A5 | **Límite de recepción por línea de OC (sin tolerancia de cantidad)** | Almacén recibe exacto lo pendiente o menos (parcial permitido), nunca más: 422 `RECEPCION_EXCEDE_PENDIENTE`. Se retiró la tolerancia por material (`tolerancia_cantidad_porcentaje`) y no hay autorización de supervisor para exceder. Actualizado 19-oct-2026 (86e3tab1k): la única tolerancia es de dinero por proveedor; fuente: plano CxP 1.7.1 y junta del 8-oct. | — |
| A6 | **Umbral de "captura sin sesgo"** | Cantidad teórica oculta al contador en la pantalla de captura. Solo visible para el aprobador al cerrar. Implementación: campo `cantidad_teorica` en `lineas_conteo` no se envía al endpoint de captura del contador. | Estándar; no negociable según §7.6. |
| A7 | **Umbral de recuento obligatorio en inventario físico** | Variación > 5% en cantidad **o** > $1,000 MXN en valor → recuento obligatorio (§7.4 del levantamiento). Configurable como parámetro global del módulo. | Confirmar con Finanzas. |
| A8 | **Política de autorización de ajustes** (§10.4 levantamiento) | Por monto: pequeño (< $1,000 MXN) Almacenista, mediano (1K–10K) Supervisor, grande (> 10K) Jefe Almacén + notificación Finanzas. Umbrales configurables. | Confirmar con Finanzas antes del go-live. |
| A9 | **Costo histórico para devoluciones de salida (8.A)** | Restitución al **costo de la salida original** (no al promedio actual). Preserva consistencia contable. Implementación: snapshot `costo_unitario` en `LineaMovimiento` al momento de la salida. | Si Finanzas prefiere costo promedio actual, se cambia. |
| A10 | **Costo histórico para devoluciones a proveedor (8.B)** | Descuento al **costo de la recepción original**. Snapshot en el movimiento de salida hacia proveedor. | Si Finanzas prefiere costo promedio actual, se cambia. |
| A11 | **Diferencia de precio variante B** | Cuando llega la factura por CxP y el precio difiere de la OC, CxP emite `DiferenciaPrecioFacturaDetectadaEvent`. Almacén suscribe y aplica **ajuste de costo del inventario remanente proporcional** + registra `MovimientoInventario` tipo `AjustePrecioFactura` con concepto contable `VARIACION_PRECIO_INVENTARIO`. | Si Finanzas prefiere cuenta de gasto financiero en lugar de ajustar inventario, se cambia el efecto. |
| A12 | **Idempotencia en consumo de eventos** | Tabla `almacen.eventos_procesados` con `(evento_id, evento_tipo, procesado_at)` para dedupe. Cada listener verifica antes de procesar. | Estándar; no negociable. |
| A13 | **Parser de Excel para carga masiva del catálogo** | MVP arranca con seed desde SAP via SQL script (no UI de carga masiva). Carga masiva diferida a vNext. | Si Carlos exige UI de carga masiva al go-live, se agrega como F1-PR2. |
| A14 | **Vale para salidas urgentes — plazo de regularización** | 48h hábiles. Después → notificación día 1 al Coordinador, día 2 al Jefe Almacén. Sin bloqueo automático. | Confirmar con Carlos plazo y consecuencias exactas. |
| A15 | **Material dañado en devolución 8.A** | Sub-almacén especial `MATERIAL_EN_REVISION` (uno por sucursal). El responsable de Calidad decide después si va a destrucción (movimiento `BajaPorDano`) o reincorporación (movimiento `ReincorporacionTrasRevision`). | Confirmar con Calidad. |
| A16 | **Cierre de mes — fecha cut-off** | Primer día hábil del mes siguiente, 23:59:59 hora MX. El Jefe Almacén dispara el cierre desde UI. Finanzas debe esperar. | Confirmar con Finanzas. |
| A17 | **Auditor externo — acceso temporal** | Rol `AuditorExterno` con permiso `almacen.lectura_total` con ventana de fechas configurable (vigencia desde/hasta). Sin permisos de captura. | Estándar. |
| A18 | **Bloqueo de salidas durante inventario anual** | Sub-almacén bajo conteo anual no permite salidas (entradas sí). Bloqueo automático al pasar conteo a `EnCurso`; se libera al `Aplicado`. | Confirmar con Carlos. |
| A19 | ✅ **Reservas de stock en MVP** (Decidido 2026-05-22, opción B del teardown de Compras) | Tabla `almacen.reservas_stock` + columna `cantidad_reservada` en `saldos_inventario`. Cuando Requisiciones transmite una RQ aprobada, llama a `IAlmacenReservaPort.Reservar(...)` que: (1) valida `cantidad_disponible = cantidad - cantidad_reservada >= solicitado`; (2) crea registro en `reservas_stock` con estado `Activa`; (3) incrementa `cantidad_reservada`; (4) emite `StockReservadoEvent`. Al surtir la RQ desde Almacén (F4), el handler consume la reserva (estado `Consumida`) y decrementa ambos `cantidad` y `cantidad_reservada` por la misma cantidad en la transacción de salida. Al cancelar la RQ, Requisiciones llama a `Liberar(reservaId)` que pone reserva en `Liberada` y decrementa solo `cantidad_reservada`. | — |

---

## 4. Modelo del dominio

### 4.1 Agregados raíz

| Agregado | Entidades hijas | Esquema | Razón de ser agregado raíz |
|---|---|---|---|
| `Almacen` | `SubAlmacen` | `almacen` | Master local del módulo. CRUD bajo `almacen.*.administrar`. Ciclo independiente. |
| `MovimientoInventario` | `LineaMovimiento`, `AdjuntoMovimiento` | `almacen` | Unidad atómica de cambio de inventario. Inmutable post-firma. Disparador de eventos. |
| `Recepcion` | (extiende `MovimientoInventario` con datos de recepción) | `almacen` | Caso especial de `MovimientoInventario` tipo `EntradaCompra` con vinculación a OC + factura/packing list. Modelado como agregado dedicado para preservar la conciliación con factura (variante B). |
| `Salida` | (extiende `MovimientoInventario`) | `almacen` | Caso especial con vinculación a RQ o Vale. Maneja regularización del Vale. |
| `DevolucionInterna` (sub-flujo 8.A) | — | `almacen` | Reversa parcial de una `Salida`. |
| `DevolucionAProveedor` (sub-flujo 8.B) | (extiende `MovimientoInventario` tipo `SalidaPorDevolucionAProveedor`) | `almacen` | Movimiento de salida hacia el proveedor; cruza con CxP vía evento. |
| `ConteoInventario` | `LineaConteo`, `RecuentoConteo` | `almacen` | Conteo rotativo o anual. Snapshot inmutable de cantidades teóricas al iniciar. |
| `SaldoInventario` | — | `almacen` | Tabla materializada (A4) por `(articulo_id, sub_almacen_id)`. Actualizada en transacción con cada movimiento. Incluye `cantidad`, `cantidad_reservada` y `costo_promedio_mxn`. `cantidad_disponible = cantidad - cantidad_reservada` (computed). |
| `ReservaStock` | — | `almacen` | Reserva activa contra `(articulo, sub_almacen)` con FK al documento origen (RQ, OC, etc.). Estados: `Activa`, `Consumida` (al surtir), `Liberada` (al cancelar). Ciclo independiente del movimiento. A19. |

### 4.2 Value Objects

| VO | Propósito | Validaciones |
|---|---|---|
| `FolioMovimiento` | Folio interno del movimiento | Formato `M-{tipo_prefijo}{año}-{secuencial:6}` (A2) |
| `CantidadInventario` | Cantidad con unidad de medida | `> 0` para movimientos firmes; `>= 0` para borrador |
| `CostoUnitario` | Costo en MXN con 4 decimales | `>= 0` |
| `MotivoMovimiento` | Catálogo de motivos por tipo | Enum según contexto (ajuste positivo, negativo, devolución por defecto, etc.) |
| `EstadoMovimiento` | Ciclo de vida | `Borrador` / `Validado` / `Registrado` (firme) / `Cancelado` (solo desde Borrador) |
| `TipoMovimiento` | Discriminador del tipo de movimiento | `EntradaCompra` / `SalidaConsumo` / `SalidaPorVale` / `DevolucionSalida` / `SalidaPorDevolucionAProveedor` / `AjustePositivo` / `AjusteNegativo` / `AjustePrecioFactura` / `BajaPorDano` / `ReincorporacionTrasRevision` / `Traspaso` (vNext) |
| `JerarquiaAlmacen` | Ruta completa Sucursal → Almacén → Sub-almacén | Validación de FKs en cadena |

### 4.3 Invariantes principales

- `MovimientoInventario` en estado `Registrado` es **inmutable**. Corrección por contramovimiento.
- `SaldoInventario.cantidad >= 0` siempre. Una salida que dejaría saldo negativo se rechaza al validar.
- `MovimientoInventario.estado = Registrado` actualiza `SaldoInventario` en la misma transacción.
- `Recepcion.oc_id` debe estar `Autorizada` y no `Cancelada` ni `Cerrada`.
- `Recepcion.cantidad_recibida <= pendiente de la línea de OC` (solicitada menos lo ya recibido); si excede, 422 `RECEPCION_EXCEDE_PENDIENTE` (A5).
- `Salida.rq_id` (variante A) debe estar `Aprobada`. Si es Vale (variante B), `rq_id` es null + `pendiente_regularizacion = true` con `fecha_limite_regularizacion = fecha_salida + 48h`.
- `DevolucionInterna.salida_origen_id` debe existir y `cantidad_devuelta <= salida_origen.cantidad - devoluciones_previas`.
- `DevolucionAProveedor` requiere autorización de Dirección (vía evidencia, mismo patrón que `NotaCargo` en CxP).
- `DevolucionAProveedor` (8.B): al registrar la salida, por cada línea de OC `cantidad_facturada <= MAX(cantidad, cantidad_recibida − devuelto (acumulado de la devolución))` (misma invariante que el CHECK `ck_oc_lineas_facturada_max` de Compras); si no, error `DEVOLUCION_EXCEDE_NO_FACTURADO` (422): solo ocurre con excedente de recepción facturado y se resuelve cancelando esa factura en CxP. Devolver lo facturado dentro de lo pedido se permite: orden **salida física → `NotaCargo` en CxP → NC fiscal tipo 03 la formaliza (compara contra el subtotal de la NC) → `ConciliadaConNcFiscal`** (corrección 30-oct; antes se pedía la NC primero y ninguna NC bajaba lo facturado). Se valida solo al registrar la salida (no al iniciar). Compras, si aun así recibe un evento que violaría esto, omite la línea, registra error y marca el evento procesado.
- `ConteoInventario.estado = Aprobado` requiere que todas las líneas con `variacion > umbral` (A7) tengan al menos un recuento registrado.
- `ConteoInventario.estado = Aplicado` genera movimientos `AjustePositivo` / `AjusteNegativo` por línea y bloquea el conteo (no se reabre).
- `SaldoInventario.cantidad_reservada >= 0` y `SaldoInventario.cantidad >= cantidad_reservada` siempre (la reserva no puede exceder el físico).
- `ReservarStockCommand` rechaza si `cantidad_disponible < solicitado`.
- `ReservaStock.estado = Activa` permite consumo o liberación. `Consumida` y `Liberada` son terminales.
- Cuando `ReservaStock` pasa a `Consumida` (al surtir), la transacción de salida decrementa `cantidad` y `cantidad_reservada` por la misma cantidad. Cuando pasa a `Liberada`, solo decrementa `cantidad_reservada`.

---

## 5. Esquema PostgreSQL

### 5.1 Tablas principales

```sql
-- Catálogo: estructura física (3 niveles + bin opcional vNext)
CREATE TABLE almacen.almacenes (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    codigo                      text NOT NULL,                    -- 'COTOLENGO-REV-1'
    nombre                      text NOT NULL,
    sucursal_id                 uuid NOT NULL,                    -- FK a Administracion.Sucursales
    es_activo                   boolean NOT NULL DEFAULT true,
    -- Auditoría
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz,
    CONSTRAINT ux_almacen_codigo UNIQUE (codigo)
);

CREATE TABLE almacen.sub_almacenes (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    almacen_id                  uuid NOT NULL REFERENCES almacen.almacenes(id),
    codigo                      text NOT NULL,                    -- 'ACC-CIR'
    nombre                      text NOT NULL,
    tipo                        text NOT NULL,                    -- 'Insumos','MaterialesDirectos','MaterialEnRevision','Trasitorio'
    es_activo                   boolean NOT NULL DEFAULT true,
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz,
    CONSTRAINT ux_sub_almacen UNIQUE (almacen_id, codigo)
);

-- Movimientos (tabla raíz polimórfica con discriminador)
CREATE TABLE almacen.movimientos_inventario (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    folio                       text NOT NULL,                    -- 'M-ENT2026-000001'
    tipo                        text NOT NULL,                    -- discriminador
    estado                      text NOT NULL,                    -- 'Borrador','Validado','Registrado','Cancelado'
    sub_almacen_id              uuid NOT NULL REFERENCES almacen.sub_almacenes(id),
    fecha_movimiento            date NOT NULL,                    -- fecha contable del movimiento
    fecha_registro              timestamptz NOT NULL DEFAULT now(),
    empresa_id                  uuid NOT NULL,
    -- Vinculaciones según tipo
    oc_id                       uuid,                             -- Recepción: FK OC en Compras
    oc_linea_id                 uuid,                             -- Recepción: FK línea de OC
    factura_id                  uuid,                             -- Variante A: FK factura en CxP (opcional)
    cfdi_recibido_id            uuid,                             -- Variante A: FK al CfdiRecibido
    packing_list_blob_ref       text,                             -- Variante B: blob del packing list
    rq_id                       uuid,                             -- Salida: FK requisición en Compras
    vale_blob_ref               text,                             -- Salida por Vale: blob del vale firmado
    salida_origen_id            uuid REFERENCES almacen.movimientos_inventario(id), -- Devolución interna: FK a salida original
    recepcion_origen_id         uuid REFERENCES almacen.movimientos_inventario(id), -- Devolución a proveedor: FK a recepción
    conteo_id                   uuid,                             -- Ajuste por inventario: FK a conteo
    proveedor_id                uuid,                             -- Devolución a proveedor: FK Proveedor
    -- Vale: regularización
    pendiente_regularizacion    boolean NOT NULL DEFAULT false,
    fecha_limite_regularizacion timestamptz,
    rq_regularizadora_id        uuid,                             -- llena al regularizar
    -- Solicitante / destinatario (Salida)
    persona_destinataria_id     uuid,                             -- FK Empleado
    maquina_destino_id          uuid,                             -- FK opcional Producción/Mantenimiento
    comentario_libre            text,
    -- Devolución
    motivo                      text,
    estado_material             text,                             -- 'Integro','UsadoParcial','Danado' (8.A)
    -- Diferencias de precio (A11)
    factura_id_origen_diff      uuid,                             -- evento DiferenciaPrecioFacturaDetectada
    -- Auditoría
    registrado_por              uuid,                             -- usuario que registró
    registrado_at               timestamptz,
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz,
    CONSTRAINT ux_movimiento_folio UNIQUE (folio),
    CONSTRAINT ck_tipo_valido CHECK (tipo IN ('EntradaCompra','SalidaConsumo','SalidaPorVale','DevolucionSalida','SalidaPorDevolucionAProveedor','AjustePositivo','AjusteNegativo','AjustePrecioFactura','BajaPorDano','ReincorporacionTrasRevision')),
    CONSTRAINT ck_estado_valido CHECK (estado IN ('Borrador','Validado','Registrado','Cancelado'))
);

-- Líneas de un movimiento
CREATE TABLE almacen.lineas_movimiento (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    movimiento_id               uuid NOT NULL REFERENCES almacen.movimientos_inventario(id) ON DELETE CASCADE,
    posicion                    int NOT NULL,
    articulo_id                 uuid NOT NULL,                    -- FK a Articulo en DatosMaestros
    cantidad                    numeric(14,4) NOT NULL,
    unidad_medida               text NOT NULL,                    -- snapshot al momento del movimiento
    costo_unitario_mxn          numeric(14,4) NOT NULL,           -- precio de OC (entrada) o costo promedio (salida; snapshot)
    monto_total_mxn             numeric(14,2) NOT NULL,           -- cantidad * costo_unitario
    moneda_original             text NOT NULL DEFAULT 'MXN',
    tipo_cambio_aplicado        numeric(10,4),                    -- NULL si MXN
    -- Recepción (variante A): para variante B se llena al conciliar
    linea_factura_id            uuid,
    -- Salida: para imputar a centro de costo
    centro_costo_id             uuid,
    proyecto_id                 uuid,
    -- Conteo: si este movimiento se generó por ajuste de inventario
    cantidad_teorica_al_contar  numeric(14,4),                    -- contra qué se ajustó
    cantidad_real_contada       numeric(14,4),
    -- Ubicación de referencia (texto libre, sin catálogo MVP)
    ubicacion_referencia        text,
    comentario_linea            text,
    CONSTRAINT ck_cantidad_positiva CHECK (cantidad > 0),
    CONSTRAINT ck_costo_no_negativo CHECK (costo_unitario_mxn >= 0)
);

-- Adjuntos
CREATE TABLE almacen.adjuntos_movimiento (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    movimiento_id               uuid NOT NULL REFERENCES almacen.movimientos_inventario(id) ON DELETE CASCADE,
    tipo                        text NOT NULL,                    -- 'Factura','PackingList','Vale','Foto','ComprobanteSalidaFirmado','Otro'
    nombre_archivo              text NOT NULL,
    blob_ref                    text NOT NULL,
    tamano_bytes                bigint,
    mime_type                   text,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL
);

-- Saldo materializado (A4 + A19)
CREATE TABLE almacen.saldos_inventario (
    sub_almacen_id              uuid NOT NULL REFERENCES almacen.sub_almacenes(id),
    articulo_id                 uuid NOT NULL,
    cantidad                    numeric(14,4) NOT NULL DEFAULT 0,
    cantidad_reservada          numeric(14,4) NOT NULL DEFAULT 0,
    cantidad_disponible         numeric(14,4) GENERATED ALWAYS AS (cantidad - cantidad_reservada) STORED,
    costo_promedio_mxn          numeric(14,4) NOT NULL DEFAULT 0,
    valor_inventario_mxn        numeric(14,2) GENERATED ALWAYS AS (cantidad * costo_promedio_mxn) STORED,
    ultima_actualizacion_at     timestamptz NOT NULL DEFAULT now(),
    ultimo_movimiento_id        uuid REFERENCES almacen.movimientos_inventario(id),
    PRIMARY KEY (sub_almacen_id, articulo_id),
    CONSTRAINT ck_cantidad_no_negativa CHECK (cantidad >= 0),
    CONSTRAINT ck_reservada_no_negativa CHECK (cantidad_reservada >= 0),
    CONSTRAINT ck_reservada_no_excede CHECK (cantidad_reservada <= cantidad)
);

-- Reservas de stock (A19)
CREATE TABLE almacen.reservas_stock (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    sub_almacen_id              uuid NOT NULL REFERENCES almacen.sub_almacenes(id),
    articulo_id                 uuid NOT NULL,
    cantidad                    numeric(14,4) NOT NULL,
    documento_origen_tipo       text NOT NULL,                    -- 'Requisicion', 'OrdenCompra', etc.
    documento_origen_id         uuid NOT NULL,
    linea_origen_id             uuid,                             -- FK opcional a línea específica
    estado                      text NOT NULL,                    -- 'Activa','Consumida','Liberada'
    motivo_liberacion           text,                             -- si Liberada
    movimiento_consumo_id       uuid REFERENCES almacen.movimientos_inventario(id), -- si Consumida
    -- Auditoría
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz,
    CONSTRAINT ck_cantidad_positiva CHECK (cantidad > 0),
    CONSTRAINT ck_estado_valido CHECK (estado IN ('Activa','Consumida','Liberada')),
    CONSTRAINT ck_consumida_tiene_movimiento CHECK (
        (estado = 'Consumida' AND movimiento_consumo_id IS NOT NULL) OR
        (estado != 'Consumida')
    ),
    CONSTRAINT ck_liberada_tiene_motivo CHECK (
        (estado = 'Liberada' AND motivo_liberacion IS NOT NULL) OR
        (estado != 'Liberada')
    )
);

-- Conteos de inventario
CREATE TABLE almacen.conteos_inventario (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tipo                        text NOT NULL,                    -- 'Rotativo','Anual'
    estado                      text NOT NULL,                    -- 'Planificado','EnCurso','EnConciliacion','Aprobado','Aplicado','Rechazado'
    sub_almacen_id              uuid REFERENCES almacen.sub_almacenes(id), -- NULL = todos los sub-almacenes
    filtro_familia              text,                             -- 'Insumos','Refacciones', NULL = todos
    fecha_planificada           date NOT NULL,
    fecha_inicio                timestamptz,
    fecha_cierre                timestamptz,
    responsable_id              uuid NOT NULL,                    -- Empleado
    snapshot_capturado_at       timestamptz,                      -- cuando se tomó el snapshot
    aprobador_id                uuid,                             -- Quien aprobó los ajustes
    fecha_aprobacion            timestamptz,
    motivo_rechazo              text,                             -- si fue Rechazado
    -- Auditoría
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz
);

CREATE TABLE almacen.lineas_conteo (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    conteo_id                   uuid NOT NULL REFERENCES almacen.conteos_inventario(id),
    articulo_id                 uuid NOT NULL,
    sub_almacen_id              uuid NOT NULL REFERENCES almacen.sub_almacenes(id),
    cantidad_teorica            numeric(14,4) NOT NULL,           -- snapshot al iniciar
    costo_promedio_snapshot     numeric(14,4) NOT NULL,           -- snapshot al iniciar
    cantidad_real_capturada     numeric(14,4),
    capturado_por               uuid,
    capturado_at                timestamptz,
    requiere_recuento           boolean NOT NULL DEFAULT false,   -- variación > umbral (A7)
    aprobado_individualmente    boolean NOT NULL DEFAULT false,
    justificacion               text,
    CONSTRAINT ux_conteo_articulo UNIQUE (conteo_id, articulo_id, sub_almacen_id)
);

CREATE TABLE almacen.recuentos_conteo (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    linea_conteo_id             uuid NOT NULL REFERENCES almacen.lineas_conteo(id),
    secuencia                   int NOT NULL,                     -- 1, 2, 3 (recuentos sucesivos)
    cantidad_recontada          numeric(14,4) NOT NULL,
    capturado_por               uuid NOT NULL,
    capturado_at                timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_recuento_secuencia UNIQUE (linea_conteo_id, secuencia)
);

-- Bloqueos por inventario anual (A18)
CREATE TABLE almacen.bloqueos_inventario (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    conteo_id                   uuid NOT NULL REFERENCES almacen.conteos_inventario(id),
    sub_almacen_id              uuid NOT NULL REFERENCES almacen.sub_almacenes(id),
    bloquea_salidas             boolean NOT NULL DEFAULT true,
    bloquea_entradas            boolean NOT NULL DEFAULT false,
    activo                      boolean NOT NULL DEFAULT true,
    desde                       timestamptz NOT NULL,
    hasta                       timestamptz
);

-- Periodos cerrados de Almacén (sincroniza con calendario fiscal de Finanzas)
CREATE TABLE almacen.periodos_cerrados (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    año                         int NOT NULL,
    mes                         int NOT NULL,
    cerrado_at                  timestamptz NOT NULL,
    cerrado_por                 uuid NOT NULL,
    CONSTRAINT ux_periodo UNIQUE (año, mes),
    CONSTRAINT ck_mes_valido CHECK (mes BETWEEN 1 AND 12)
);

-- Idempotencia de eventos consumidos (A12)
CREATE TABLE almacen.eventos_procesados (
    evento_id                   uuid NOT NULL,
    evento_tipo                 text NOT NULL,
    procesado_at                timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (evento_id, evento_tipo)
);

-- Outbox (patrón ADR-0009)
CREATE TABLE almacen.outbox_messages (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    occurred_at                 timestamptz NOT NULL DEFAULT now(),
    type                        text NOT NULL,
    payload                     jsonb NOT NULL,
    published_at                timestamptz,
    correlation_id              uuid,
    aggregate_id                uuid
);
```

### 5.2 Índices críticos

```sql
-- Bandeja "Recepciones del día" por sub-almacén
CREATE INDEX ix_movimientos_recepciones
  ON almacen.movimientos_inventario (sub_almacen_id, fecha_movimiento DESC)
  WHERE tipo = 'EntradaCompra' AND estado = 'Registrado';

-- Bandeja "Salidas pendientes de regularización" (Vale)
CREATE INDEX ix_movimientos_vale_pendientes
  ON almacen.movimientos_inventario (fecha_limite_regularizacion)
  WHERE tipo = 'SalidaPorVale' AND pendiente_regularizacion = true;

-- Saldo por sub-almacén
CREATE INDEX ix_saldos_sub_almacen
  ON almacen.saldos_inventario (sub_almacen_id, articulo_id);

-- Saldo con stock disponible (para selectores de salida)
CREATE INDEX ix_saldos_con_stock
  ON almacen.saldos_inventario (articulo_id, sub_almacen_id)
  WHERE cantidad > 0;

-- Bandeja "Conteos en curso"
CREATE INDEX ix_conteos_activos
  ON almacen.conteos_inventario (estado, fecha_planificada)
  WHERE estado IN ('Planificado','EnCurso','EnConciliacion');

-- Movimientos por OC (para sincronía con Compras)
CREATE INDEX ix_movimientos_por_oc
  ON almacen.movimientos_inventario (oc_id, tipo)
  WHERE oc_id IS NOT NULL;

-- Salidas por RQ
CREATE INDEX ix_movimientos_por_rq
  ON almacen.movimientos_inventario (rq_id, tipo)
  WHERE rq_id IS NOT NULL;

-- Reservas activas por documento origen (lookup al cancelar/surtir RQ)
CREATE INDEX ix_reservas_por_documento
  ON almacen.reservas_stock (documento_origen_tipo, documento_origen_id)
  WHERE estado = 'Activa';

-- Reservas activas por articulo + sub-almacen (cálculo de cantidad_reservada)
CREATE INDEX ix_reservas_activas_articulo
  ON almacen.reservas_stock (articulo_id, sub_almacen_id)
  WHERE estado = 'Activa';
```

### 5.3 Migraciones

EF Core (ADR-0005). La migración inicial incluye seeds de:

- `almacenes` y `sub_almacenes` migrados desde SAP (script previo al go-live; ver §12 del levantamiento).
- `periodos_cerrados` con todos los meses anteriores al go-live cerrados.
- Sub-almacenes especiales: `MATERIAL_EN_REVISION` (A15) — uno por sucursal.

---

## 6. Puertos y adaptadores

### 6.1 Puertos de lectura (Almacén consume)

| Puerto | Quién implementa | Uso |
|---|---|---|
| `IComprasOcReadPort` | Adapter en `Compras.Infrastructure` | Verificar OC autorizada al recibir; obtener detalle de líneas |
| `IComprasRequisicionReadPort` | Adapter en `Compras.Infrastructure` | Verificar RQ aprobada al surtir; obtener detalle de líneas |
| `IArticuloReadPort` | Adapter en `DatosMaestros.Infrastructure` | Master de artículos: UM, conversión, sub-almacén default (sin tolerancia de cantidad, A5) |
| `IProveedorReadPort` | Adapter en `DatosMaestros.Infrastructure` | Para devolución a proveedor (8.B): obtener datos del proveedor. Incluye `ObtenerPorIdsAsync` batch (ADR-0042) para enriquecer bandeja/detalle de devoluciones con la razón social |
| `ICxpDocumentosReadPort` | Adapter en `CuentasPorPagar.Infrastructure.PublicAdapters` | **Primer puerto de lectura Almacén → CxP** (solo presentación, ADR-0042): resuelve `facturaId → folio del proveedor`, `cfdiRecibidoId → UUID fiscal del SAT` y `notaCreditoId → folio` para el detalle de recepción y de devolución 8.B. NO habilita lógica de negocio cruzada — la conciliación de la triada sigue viajando exclusivamente por eventos |
| `ISucursalReadPort` | Adapter en `Administracion.Infrastructure` | Catálogo de sucursales |
| `IEmpleadoReadPort` | ✅ `Compartido.Infrastructure.PublicAdapters.EmpleadoReadAdapter` sobre `compartido.empleados` (ADM-PR2, doc 10 de Administración) | Catálogo de empleados (responsables de conteos, solicitante de devolución interna). El destinatario de salidas sigue por `IUsuarioReadPort` (ADR-0042) |
| `ITipoCambioReadPort` | `Compartido.Infrastructure.PublicAdapters.TipoCambioReadAdapter` (registrado en `Program.cs`, C1.2) sobre `compartido.tipos_cambio` | T/C de la fecha exacta (ADR-0014); sin T/C registrado devuelve `null`, nunca uno inventado |
| `IConceptoContableReadPort` | Adapter en `Contabilidad.Infrastructure` (stub) | Mapeo concepto → cuenta |
| `IPeriodoContableReadPort` | `Almacen.Infrastructure.PublicAdapters.PeriodoContableReadAdapter` → `Contabilidad.Application.PublicPorts.IPeriodoContableConsultaPort` | C1.2: solo periodo existente y abierto; conserva el cierre propio de inventario (D18) |

### 6.2 Puertos públicos (Almacén expone a otros módulos)

| Puerto | Consumidores | Uso |
|---|---|---|
| Eventos del Outbox | Compras, CxP, Contabilidad, Requisiciones | Publicación asíncrona de eventos de integración (§7). |
| `IAlmacenSaldoQueryPort` | Compras (módulo) | Consulta **sincrónica** de saldo por `(articulo_id, sub_almacen_id?)`. Devuelve `SaldoArticulo { cantidad, cantidad_reservada, cantidad_disponible, costo_promedio, valor }`. Reemplaza el stub `InMemoryConsultarStockPort` de Compras. Read-only sobre `saldos_inventario`. Open Host Service pattern — interface en `Almacen.Domain.Ports.Public`, adapter en `Almacen.Infrastructure.PublicAdapters`. Se introduce en F2-PR2. |
| `IAlmacenReservaPort` (A19) | Compras / Requisiciones | Operaciones de reserva: `Reservar(articulo, sub_almacen, cantidad, documento_origen)` → devuelve `reserva_id` o falla si `cantidad_disponible < solicitado`; `Liberar(reserva_id, motivo)` → libera sin consumir; `ConsultarReserva(reserva_id)` → estado actual. Reemplaza los stubs `InMemoryReservarStockPort` y `InMemoryLiberarReservaPort` de Compras. Se introduce en F3-PR2. Ver [`docs/modulos/compras-ordenes-compra/_teardown-stubs-almacen-cxp.md`](../compras-ordenes-compra/_teardown-stubs-almacen-cxp.md). |

> **Diseño del Open Host Service:** otros módulos NUNCA leen directamente `almacen.saldos_inventario`. Consumen `IAlmacenSaldoQueryPort` inyectado. Esto preserva el bounded context y permite que el schema interno de Almacén evolucione sin romper consumidores.

### 6.3 Adapters de Infrastructure

- `OcRecepcionEventPublisher` — emite `OcRecepcionRegistradaEvent` via Outbox.
- `OcDevolucionEventPublisher` — emite `OcDevolucionRegistradaEvent`.
- `ExcelExporter` — genera ALFAK-HISTORIAL y MP-CNK exportables (server-side via `exceljs`-equivalente .NET, o pre-render en frontend con datos JSON; ver ADR-0036).
- `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<AlmacenDbContext>` — reutilizado del patrón Compras/CxP.

---

## 7. Eventos de integración

### 7.1 Eventos publicados por Almacén

Patrón Outbox + Service Bus (ADR-0009). Naming canónico `{Agregado}{Verbo}Event` alineado con Compras-OC §8.5.

| Evento | Trigger | Consumidor primario | Payload mínimo |
|---|---|---|---|
| `OcRecepcionRegistradaEvent` | `Recepcion.estado = Registrado` | **Compras** (sub-estado OC), **CxP** (conciliación) | `oc_id`, `oc_linea_id`, `cantidades[]`, `factura_pendiente` (var. B), `cfdi_recibido_id?` (var. A), `merma?`, `observaciones?` |
| `OcDevolucionRegistradaEvent` | `DevolucionAProveedor.estado = Registrado` (sub-flujo 8.B) | **CxP** (genera `NotaCargo`), **Compras** (decrementa `CantidadRecibida`) | `proveedor_id`, `recepcion_origen_id`, `factura_origen_id?`, `motivo`, `lineas`, `evidencias` |
| `SalidaRequisicionRegistradaEvent` | `Salida.estado = Registrado` (normal o por vale) | **Contabilidad**, **Requisiciones** (actualiza saldo de RQ) | `rq_id?`, `vale_id?`, `lineas[]`, `centro_costo_id`, `persona_destinataria_id` |
| `DevolucionInternaAplicadaEvent` | `DevolucionInterna.estado = Registrado` (sub-flujo 8.A) | **Contabilidad** | `salida_origen_id`, `lineas[]`, `costo_revertido` |
| `AjusteInventarioAplicadoEvent` | `ConteoInventario.estado = Aplicado` (movimientos de ajuste generados) | **Contabilidad** | `conteo_id`, `movimientos_generados[]`, `monto_neto_mxn` |
| `EntradaInventarioValoradaEvent` | `Recepcion.estado = Registrado` (asentar entrada al inventario) | **Contabilidad** | `movimiento_id`, `lineas[]`, `monto_total_mxn`, `cuenta_inventario`, `cuenta_contraparte` |
| `StockReservadoEvent` (A19) | `ReservarStockCommand` exitoso | **Compras** (informativo; ya sabe porque hizo la llamada sincrónica), **Contabilidad** (informativo) | `reserva_id`, `articulo_id`, `sub_almacen_id`, `cantidad`, `documento_origen` |
| `StockLiberadoEvent` (A19) | `LiberarReservaCommand` exitoso | **Compras** (informativo), **Contabilidad** (informativo) | `reserva_id`, `motivo` |

### 7.2 Eventos suscritos por Almacén

| Evento | Origen | Acción |
|---|---|---|
| `OrdenCompraAutorizadaEvent` | Compras | Habilita la OC para recepción (proyección local opcional). |
| `OrdenCompraCanceladaEvent` | Compras | Si hay recepciones en borrador asociadas → notifica al Almacenista. |
| `OrdenCompraCerradaEvent` | Compras | Informativo. |
| `FacturaProveedorRegistradaEvent` | CxP (variante B) | Marca la `Recepcion` correspondiente como `ConciliadaConFactura` cuando llega la factura del proveedor. |
| `DiferenciaPrecioFacturaDetectadaEvent` | CxP | Genera movimiento `AjustePrecioFactura` y ajusta costo del inventario remanente (A11). |
| `NotaCreditoFiscalDevolucionRecibidaEvent` | CxP | Marca la `DevolucionAProveedor` (sub-flujo 8.B) como `ConciliadaConNcFiscal`. |
| `RequisicionAprobadaEvent` | Requisiciones | Habilita la RQ para surtido. |
| `RequisicionCanceladaEvent` | Requisiciones | Si hay salidas en borrador → notifica al Almacenista. |
| `PeriodoContableCerradoEvent` | Finanzas (futuro) | Bloquea operaciones con fecha del periodo cerrado. |

### 7.3 Idempotencia de consumo (A12)

Cada listener consulta `almacen.eventos_procesados` por `(evento_id, evento_tipo)` antes de procesar. Si ya está → loggea y descarta. Si no, procesa en transacción que también inserta el registro de procesado (atomic).

---

## 8. Comandos y queries (CQRS)

Lista no exhaustiva pero canónica.

### 8.1 Comandos

```
Catálogo:
  CrearAlmacenCommand
  CrearSubAlmacenCommand
  ActualizarSubAlmacenCommand
  DesactivarSubAlmacenCommand

Recepción (Variante A — factura):
  IniciarRecepcionConFacturaCommand
  AgregarLineaRecepcionCommand
  ValidarRecepcionCommand
  RegistrarRecepcionCommand                    // pasa a firme; dispara evento
  CancelarRecepcionBorradorCommand

Recepción (Variante B — packing list):
  IniciarRecepcionConPackingListCommand
  AgregarLineaRecepcionCommand                 // mismo command
  ValidarRecepcionCommand
  RegistrarRecepcionCommand
  ConciliarRecepcionConFacturaCommand          // disparado por FacturaProveedorRegistradaEvent

Salida (Variante A — normal):
  IniciarSalidaConRqCommand
  AgregarLineaSalidaCommand
  ValidarSalidaCommand
  SurtirSalidaCommand                          // pasa a firme; emite evento
  CancelarSalidaBorradorCommand

Salida (Variante B — Vale):
  RegistrarSalidaPorValeCommand                // captura directa con vale firmado
  RegularizarSalidaPorValeCommand              // vincula RQ posterior

Devolución interna (8.A):
  IniciarDevolucionInternaCommand
  AgregarLineaDevolucionInternaCommand
  AplicarDevolucionInternaCommand

Devolución a proveedor (8.B):
  IniciarDevolucionAProveedorCommand
  AdjuntarEvidenciaDevolucionAProveedorCommand
  SolicitarAutorizacionDevolucionAProveedorCommand
  RegistrarSalidaDevolucionAProveedorCommand   // emite OcDevolucionRegistradaEvent

Inventario físico:
  CrearConteoInventarioCommand
  IniciarConteoCommand                         // toma snapshot
  CapturarLineaConteoCommand                   // sin ver cantidad teórica (A6)
  AgregarRecuentoCommand
  EnviarAprobacionConteoCommand
  AprobarConteoCommand
  AplicarConteoCommand                         // genera ajustes; emite evento
  RechazarConteoCommand

Cierre de mes:
  EjecutarCierreMensualCommand                 // valida + cierra periodo

Ajustes manuales:
  RegistrarAjusteManualCommand                 // restringido por permiso

Reservas de stock (A19):
  ReservarStockCommand                         // llamado vía IAlmacenReservaPort.Reservar()
  LiberarReservaCommand                        // llamado vía IAlmacenReservaPort.Liberar()
  // ConsumirReservaCommand es interno (lo dispara el handler de SurtirSalidaCommand cuando la RQ tiene reserva activa)
```

### 8.2 Queries

```
Bandejas:
  RecepcionesPorAlmacenQuery
  RecepcionesPendientesFacturaQuery            // variante B
  SalidasDelDiaQuery
  SalidasPorValePendientesRegularizarQuery
  DevolucionesPendientesAutorizacionQuery
  ConteosActivosQuery
  StockBajoMinimoQuery                         // si master de artículos tiene min/max

Detalle:
  GetMovimientoByIdQuery
  GetRecepcionByIdQuery
  GetConteoByIdQuery

Saldos:
  SaldoInventarioPorSubAlmacenQuery
  SaldoArticuloPorTodosSubAlmacenesQuery
  HistorialMovimientosArticuloQuery

Reportes (motor nativo, ADR-0036):
  ReporteAlfakHistorialAlmacenQuery
  ReporteExistenciaMpCnkQuery
```

---

## 9. Workers en-proceso (`IHostedService`)

ADR-0022. Patrón heredado de Compras/CxP.

| Worker | Schedule | Responsabilidad | NoOp condicional |
|---|---|---|---|
| `OutboxPublisherWorker<AlmacenDbContext>` | Cada 5s | Publica eventos pendientes a Service Bus | Si cs vacía |
| `RegularizacionValeSlaWorker` | Diario (9 AM) | Detecta vales sin regularizar (A14): día 1 → notifica Coordinador, día 2 → notifica Jefe Almacén | Si `INotificacionService` no está configurado |
| `InventarioRotativoCalendarioWorker` | Diario (3 AM) | Sugiere conteos rotativos según calendario configurado por sub-almacén | Sin condición externa |

---

## 10. RBAC — permisos canónicos

```
almacen.almacenes.read
almacen.almacenes.administrar            // CRUD almacenes y sub-almacenes

almacen.entradas.read
almacen.entradas.capturar
almacen.entradas.registrar               // firmar
almacen.entradas.cancelar_borrador

almacen.salidas.read.propias             // Empleado: solo sus salidas como responsable
almacen.salidas.read.todas
almacen.salidas.capturar
almacen.salidas.registrar
almacen.salidas.por_vale                 // permiso especial; auditable

almacen.devoluciones.internas.read
almacen.devoluciones.internas.capturar
almacen.devoluciones.proveedor.iniciar
almacen.devoluciones.proveedor.autorizar // Dirección (compartido con CxP.notas_cargo.autorizar)
almacen.devoluciones.proveedor.registrar

almacen.inventarios.read
almacen.inventarios.crear
almacen.inventarios.capturar
almacen.inventarios.aprobar.nivel1       // Almacenista (variación pequeña)
almacen.inventarios.aprobar.nivel2       // Supervisor / Jefe Almacén (mediana)
almacen.inventarios.aprobar.nivel3       // Jefe Almacén + notificación Finanzas (grande)

almacen.ajustes.manual                   // restringido

almacen.reportes.alfak
almacen.reportes.mp_cnk
almacen.reportes.movimientos             // vNext

almacen.cierre_mes.ejecutar              // Jefe Almacén

almacen.lectura_total                    // auditor externo (A17)
```

Roles operativos (asignación inicial a confirmar con Carlos):

- **Almacenista** — captura entradas/salidas/devoluciones internas; aprueba ajustes pequeños; lee saldos.
- **SupervisorInsumos** — todo lo del Almacenista + aprueba ajustes medianos + (sin autorización de recepciones fuera de límite: no existe tolerancia de cantidad).
- **AlmacenistaMaterialesDirectos** — todo lo del Almacenista, ámbito sub-almacenes de MP no-vidrio.
- **JefeAlmacen** — todo + aprueba ajustes grandes + ejecuta cierre de mes + autoriza devoluciones a proveedor (con DG).
- **AuditorExterno** — solo lectura (A17).

---

## 11. Endpoints HTTP

Versionado `/api/v1/almacen/...` (ADR-0021). Idempotency-Key obligatorio en POST que crean recursos (ADR-0020). ETag/If-Match en mutaciones (ADR-0012). Problem Details (ADR-0010).

```
Catálogos
  GET    /almacenes
  POST   /almacenes
  PATCH  /almacenes/{id}
  GET    /sub-almacenes
  POST   /sub-almacenes
  PATCH  /sub-almacenes/{id}

Movimientos (vista unificada)
  GET    /movimientos
  GET    /movimientos/{id}

Recepción
  GET    /recepciones
  GET    /recepciones/{id}
  POST   /recepciones                       — body: {variante: 'A'|'B', oc_id, lineas, ...}
  PATCH  /recepciones/{id}                  — solo Borrador
  POST   /recepciones/{id}/validar
  POST   /recepciones/{id}/registrar
  POST   /recepciones/{id}/cancelar

Salida
  GET    /salidas
  GET    /salidas/{id}
  POST   /salidas                           — body: {variante: 'Normal'|'Vale', rq_id?, ...}
  POST   /salidas/{id}/surtir
  POST   /salidas/{id}/regularizar          — para Vale (body: rq_id)

Devoluciones
  GET    /devoluciones-internas
  POST   /devoluciones-internas
  POST   /devoluciones-internas/{id}/aplicar

  GET    /devoluciones-proveedor
  POST   /devoluciones-proveedor
  POST   /devoluciones-proveedor/{id}/autorizar
  POST   /devoluciones-proveedor/{id}/registrar-salida

Inventario físico
  GET    /conteos
  POST   /conteos
  POST   /conteos/{id}/iniciar
  POST   /conteos/{id}/lineas/{linea_id}/capturar
  POST   /conteos/{id}/lineas/{linea_id}/recuento
  POST   /conteos/{id}/enviar-aprobacion
  POST   /conteos/{id}/aprobar
  POST   /conteos/{id}/aplicar
  POST   /conteos/{id}/rechazar

Saldos
  GET    /saldos                            — por sub-almacén, paginado
  GET    /saldos/articulo/{articulo_id}     — todos los sub-almacenes
  GET    /movimientos/articulo/{articulo_id} — histórico

Cierre de mes
  POST   /cierre-mes                        — body: {año, mes}

Reportes
  GET    /reportes/alfak-historial-almacen
  GET    /reportes/mp-cnk
```

---

## 12. Frontend — patrones aplicables

Memoria `project_estructura_ventanas_erp` + [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md). Almacén replica los patrones de Compras-OC.

### 12.1 Master-detail por recurso

- `/almacen/movimientos` — bandeja unificada con filtros (tipo, estado, sub-almacén, periodo).
- `/almacen/movimientos/$id` — detalle.
- `/almacen/recepciones` — bandeja específica de recepciones.
- `/almacen/salidas` — bandeja de salidas.
- `/almacen/conteos` — bandeja de conteos en curso.
- `/almacen/saldos` — vista de inventario con filtros.

### 12.2 Sheets para "Nueva..."

- "Nueva recepción" — sheet con selector de OC; muestra líneas pre-cargadas con cantidad solicitada y campo cantidad recibida; flag variante A/B en el body.
- "Nueva salida" — sheet con selector de RQ o captura directa de Vale.
- "Nueva devolución" — sheet con selector de salida origen (8.A) o recepción origen (8.B).
- "Nuevo conteo" — sheet con selector de sub-almacén, familia, tipo.

### 12.3 Pantalla de captura de conteo (caso especial)

Patrón único del módulo. Vista de scroll vertical con línea por artículo:

- Columna izquierda: clave + descripción + sub-almacén + ubicación de referencia.
- Columna derecha: input numérico de cantidad real. **No** muestra cantidad teórica (A6).
- Botón "Marcar para recuento" si el contador detecta una variación visible.
- Footer: "X de N líneas capturadas".

### 12.4 Pantalla de aprobación de conteo

Vista comparativa por línea:

- Cantidad teórica | Cantidad real | Variación absoluta | Variación % | Variación valor | Acción.
- Líneas con `requiere_recuento = true` se destacan en amarillo si no se ha capturado recuento.
- Bulk approve: aprobar todas las variaciones bajo umbral con un click.

### 12.5 Topbar

- Selector global de sub-almacén activo (chip).
- Quick Create: "Nueva recepción", "Nueva salida", "Nuevo movimiento sin RQ" (gated).

---

## 13. Dependencias de plataforma pendientes

Ver §14 del [00-levantamiento](00-levantamiento.md#14-dependencias-de-plataforma-pendientes). Resumen de los stubs / NoOps que este diseño introduce:

- `NoOpComprasOcReadPort` — devuelve OC fija de fixture en dev. `PLATFORM-TODO(<ComprasOcReadPort>)`.
- `NoOpComprasRequisicionReadPort` — fixture. `PLATFORM-TODO(<ComprasRqReadPort>)`.
- `NoOpCxpEventConsumer` — registra eventos consumidos en log. `PLATFORM-TODO(<CxpEvents>)`.
- `NoOpContabilidadEventPublisher` — loggea sin emitir. `PLATFORM-TODO(<ContabilidadEvents>)`.
- C1.2 (07-oct-2026, implementación local): retirado `NoOpPeriodoContableReadPort`; el adaptador real se registra en `Almacen/Infrastructure/DependencyInjection.cs`. Recepciones con factura/packing list, salidas con requisición/vale y devoluciones internas comprueban ambos cierres. El cierre mensual de inventario no consulta Contabilidad. Evidencia y pendientes: [entrega C1.2](02-c12-periodo-contable-entrega.md).

- **Cerrado en 86e3pc50v:** `<ArticuloReadAdapter>`. `Program.cs` registra `Compartido.Infrastructure.PublicAdapters.ArticuloReadAdapter` para `IArticuloReadPort` de Almacén; el catálogo devuelve la tolerancia y la categoría reales. Los NoOp de Compras (`NoOpComprasOcReadPort`, `NoOpComprasRequisicionReadPort`) siguen en `NoOpReadPorts.cs`, pero `Program.cs` los sustituye en el arranque por `ComprasOcReadAdapter` y `ComprasRequisicionReadAdapter`.

Cada `PLATFORM-TODO` busca-able con `rg "PLATFORM-TODO" backend/src/Almacen`.

---

## 14. Riesgos técnicos

| Riesgo | Mitigación de diseño |
|---|---|
| **Acoplamiento con Compras** vía tablas compartidas. | Solo puertos de lectura `IComprasOcReadPort`, `IComprasRequisicionReadPort`. Cero acceso directo a `compras.*`. |
| **Lock contention en `saldos_inventario`** durante picos de captura. | Actualización por `UPDATE WHERE` con `RETURNING`; sin select-then-update. Si hay contention real, evaluar PostgreSQL advisory locks por `(sub_almacen, articulo)`. |
| **Re-localización del placeholder `Almacen` de `DatosMaestros`** rompe el módulo Administración. | Migración aditiva en F1: crear tablas en `almacen.*`, copy-from `compartido.almacenes`, mantener placeholder hasta confirmación. F1-PR-final hace `DROP` del placeholder con coordinación. |
| **Materialización de `saldos_inventario`** se desincroniza si una transacción falla parcialmente. | Trigger BEFORE INSERT en `movimientos_inventario` (estado `Registrado`) que valida + actualiza `saldos_inventario` en la misma transacción. Alternativa: lógica en handler con transacción explícita. Tests de integración cubren el caso. |
| **Migración SAP de catálogos** con datos inconsistentes (UM duplicadas, materiales sin sub-almacén default). | Script de validación previo al go-live con criterio "no migrar artículos sin movimiento en 2 años" (mismo criterio que Proveedores en CxP). Reporte de inconsistencias antes de aplicar. |
| **Costo promedio ponderado** con devoluciones a costo original (A9, A10) introduce divergencia con el costo actual. | Documentación contable clara; tests con escenarios de devolución; validación con auditor externo al cierre del primer mes. |
| **Captura de conteo sin sesgo** difícil de testear (no se puede ver lo que falta). | Tests E2E con dos navegadores: contador captura sin ver teórico; aprobador valida después. |
| **Snapshot de teórico al iniciar conteo rotativo** si el almacén opera durante el conteo. | Snapshot tomado en transacción al pasar `Planificado → EnCurso`. Movimientos posteriores no afectan el snapshot. Aprobador compara contra snapshot, no contra teórico actual. |
| **Bloqueo de salidas durante anual** debe ser robusto a tiempos de captura largos. | `bloqueos_inventario` consultado en cada validación de salida; la cancelación de salida en curso requiere mensaje claro al usuario. |
| **Eventos de Compras llegan antes que `OrdenCompraAutorizadaEvent`** por race condition. | Idempotencia + tabla `eventos_procesados`; al recibir `OcRecepcionRegistradaEvent` sin OC conocida, se procesa pendiente y se reintenta al recibir el evento de autorización. |
| **Bloqueo de cierre de mes** si hay movimientos pendientes de aplicar de un conteo. | Validación pre-cierre: no se permite cerrar mes con conteos `EnConciliacion` o `Aprobado` (sin Aplicar). |

---

## 15. Pendientes y próximos pasos

### 15.1 Antes del 02-plan

1. Confirmar A1-A18 con el owner (Carlos + Eduardo).
2. Política de tolerancias resuelta: solo dinero por proveedor (86e3tab1k).
3. Confirmar umbrales monetarios de autorización de ajustes (A8).
4. Validar lista exhaustiva de sucursales/almacenes/sub-almacenes en SAP (§3 del levantamiento).
5. Conseguir un export de catálogo de artículos desde SAP para validar campos requeridos.

### 15.2 Diferidos a vNext (post-MVP)

- Operación móvil con códigos de barras y escáner.
- Gestión formal de bins/ubicaciones físicas.
- Clasificación ABC + planificación automática de conteos cíclicos.
- Traspasos internos entre sub-almacenes (puede entrar al MVP si Carlos lo pide).
- Putaway y picking dirigidos.
- Reportes 3–6 del Portal Millet.
- Integración directa con cámaras IoT para conteo automatizado.

### 15.3 Coordinación con otros módulos

- **Compras-OC:** confirmar que el listener para `OcRecepcionRegistradaEvent` y `OcDevolucionRegistradaEvent` está cableado.
- **CxP:** confirmar consumo de `FacturaProveedorRegistradaEvent` con flag `factura_pendiente` (variante B) y `DiferenciaPrecioFacturaDetectadaEvent`.
- **DatosMaestros:** ampliar entidad `Articulo` con `sub_almacen_default_id`. La columna `tolerancia_cantidad_porcentaje` (86e3pc50v) se retiró en 86e3tab1k; ver §16.1.
- **Contabilidad (futuro):** definir mapeo de conceptos contables para los 7 eventos de Almacén.
- **Finanzas (futuro):** integración con calendario fiscal vía `PeriodoContableCerradoEvent`.

---

## 16. Cambios implementados en 86e3pc50v (CONT-MOD-ALM)

Esta sección describe lo que quedó implementado en la tarea 86e3pc50v (rama `fix/almacen-recepcion-tolerancia-86e3pc50v`). Donde el diseño original de §A5, §10 o §15 difiera, prevalece lo descrito aquí. Los nombres de columna, permisos, rutas y códigos se verificaron contra el código.

### 16.1 Límite de recepción por línea (A5) — sin tolerancia de cantidad

Actualizado 19-oct-2026 (86e3tab1k): la única tolerancia es de dinero por proveedor; fuente: plano CxP 1.7.1 y junta del 8-oct.

Esta sección sustituye a la versión de 86e3pc50v. Se **retiraron**: la columna `compartido.articulos.tolerancia_cantidad_porcentaje` y su CHECK, el endpoint `PUT /api/v1/datos-maestros/articulos/{id}/tolerancia-recepcion`, el permiso `datos_maestros.articulos.tolerancia-editar` y el código `RECEPCION_EXCEDE_TOLERANCIA`.

- **Límite único:** `RecepcionOcGuard.Limite(solicitada, recibidaCompras, recibidaPropia)` calcula `solicitada − max(recibidaCompras, recibidaPropia)`. El registro de la recepción y la pantalla usan esta misma función. La pantalla la consume desde `GET /api/v1/almacen/recepciones/ordenes/{ordenCompraId}/lineas-recibibles` (`LineasRecibiblesQuery`), que calcula el máximo recibible en servidor.
- **Recibido propio** (`RecibidoPorLineaOc`): entradas `EntradaCompra` registradas por `linea_oc_id`, menos las devoluciones 8.B en estado `Registrada` o `ConciliadaConNcFiscal`. Una devolución `Rechazada` no cuenta. El resultado se expresa en la unidad de la OC.
- **Concurrencia:** las recepciones con factura o con packing list abren `DevolucionOrigenLock.AbrirAsync(db, ordenCompraId)`, que ejecuta `pg_advisory_xact_lock` por OC. Serializa las recepciones de una misma OC. El lock es por OC, no por línea (ver §16.7).
- **Rechazo duro:** recibir más de lo pendiente responde 422 `RECEPCION_EXCEDE_PENDIENTE`. La recepción parcial (menos de lo pendiente) está permitida. No existe autorización de supervisor.

### 16.2 Triada con Compras y CxP (F2)

- **Línea de OC obligatoria en la recepción:** las recepciones nuevas exigen `LineaOcId` por línea; si falta responde 422 `RECEPCION_LINEA_OC_REQUERIDA`, y si el artículo no coincide con la línea responde `RECEPCION_LINEA_OC_ARTICULO_INCONGRUENTE`. Las recepciones históricas sin `LineaOcId` no se backfillean (M-036).
- **Resolución en Compras:** el consumidor de Compras resuelve las líneas sin `LineaOcId` por artículo único o falla visible sin marcar el evento como procesado (código `RECEPCION_LINEA_OC_NO_RESOLUBLE` en Compras; `DEVOLUCION_LINEA_OC_NO_RESOLUBLE` en la devolución). Ver `docs/modulos/compras-ordenes-compra/01-diseno.md` §8.5.
- **Campos aditivos de los eventos de Almacén:** `OcRecepcionRegistradaIntegrationEvent` agrega `ToleranciaPorcentaje` (obsoleto: se conserva por compatibilidad y siempre va nulo) y `CantidadRecibidaAcumulada`. `OcDevolucionRegistradaIntegrationEvent` agrega `CantidadDocumento` (unidad de la OC). Son opcionales y no implican cambio de versión.
- **Devolución 8.B:** la cantidad que se publica a Compras está en la unidad de la OC (`CantidadDocumento`), no en la unidad base del inventario.

### 16.3 CFDI de la recepción con factura (F3 y F4)

- **Listado por OC:** `GET /api/v1/almacen/recepciones/ordenes/{ordenCompraId}/cfdis` resuelve en servidor el RFC del proveedor de la OC y consulta CxP (`ListarCfdisParaRecepcionQuery`): estado `PorProcesar`, tipo `Ingreso` y RFC igual al del proveedor. Si el RFC del proveedor es nulo, genérico (`XAXX010101000` o `XEXX010101000`) o inválido, la respuesta es lista vacía con `aviso`.
- **Validación de servidor del CFDI vinculado:** el registro verifica con `ICxpDocumentosReadPort` la existencia, el estado y el tipo, y el RFC del proveedor. Los códigos son `RECEPCION_CFDI_NO_ENCONTRADO`, `RECEPCION_CFDI_NO_VINCULABLE` (estado distinto de `PorProcesar` o tipo distinto de `Ingreso`) y `RECEPCION_CFDI_PROVEEDOR_DISTINTO`.
- **Precedencia (M-074):** si llega `CfdiRecibidoId`, prevalece el repositorio de CxP y ese CFDI se valida. El folio fiscal capturado a mano (`CfdiUuidFiscal`) se guarda en el movimiento sin validarlo contra CxP; el enlace posterior con el CFDI lo hace la triada mediante `CfdiRecibidoIngresadoEvent`.
- **Sin CFDI:** si no llega ni `CfdiRecibidoId` ni `CfdiUuidFiscal`, el handler responde 422 `RECEPCION_SIN_CFDI`. La regla salió del validador FluentValidation y vive en el handler; el dominio (`MovimientoInventario`) lanza el mismo código como defensa adicional.
- **Códigos 422 alineados con la pantalla:** `SALIDA_STOCK_INSUFICIENTE` (el trigger de saldos emite `SALDO_INSUFICIENTE` o `SALDO_INEXISTENTE` y `GlobalExceptionHandler` lo traduce a 422), los códigos `CIERRE_*` y `VALE_RQ_REGULARIZADORA_INVALIDA`.

### 16.4 Regla de sucursal (F5 y ADR-0051)

- **Mismatch de sucursal:** la recepción A y B, la salida con RQ, la regularización de vale y la devolución 8.B responden 422 cuando la OC, la RQ o la recepción origen pertenece a otra sucursal que el almacén. Los códigos son `RECEPCION_SUCURSAL_DISTINTA`, `SALIDA_SUCURSAL_DISTINTA`, `VALE_SUCURSAL_DISTINTA` y `DEVOLUCION_SUCURSAL_DISTINTA`. La regla aplica aunque el usuario tenga el permiso de bypass. Los mensajes no nombran las sucursales (desviación menor de M-080, para no añadir dependencias a cinco handlers).
- **Alcance de datos:** `SucursalScopeGuard` y `DocumentoSucursalScope` acotan conteos, aprobación de conteos, devoluciones 8.A y 8.B, MAT-REV, saldos, reportes ALFAK y MP-CNK, cierre de mes, y la lectura y descarga de salidas y vales.
- **Bypass por recurso:** sin tablas ni roles por sucursal, según ADR-0051. Permisos nuevos en 86e3pc50v (`PermisosCanonicos`):
  - `almacen.inventarios.leer-todas-sucursales` y `almacen.inventarios.gestionar-todas-sucursales`
  - `almacen.devoluciones-proveedor.leer-todas-sucursales` y `almacen.devoluciones-proveedor.gestionar-todas-sucursales`
  - `almacen.devoluciones-internas.gestionar-todas-sucursales`
  - `almacen.almacenes.leer-todas-sucursales` (saldos y jerarquía)
  - `almacen.reportes.leer-todas-sucursales`
  - `almacen.cierre-mes.gestionar-todas-sucursales`
- **Permisos previos** que ya seguían el mismo patrón: `almacen.entradas.{leer,gestionar}-todas-sucursales`, `almacen.salidas.{leer,gestionar}-todas-sucursales` y `almacen.reorden.{leer,gestionar}-todas-sucursales`.
- **Usuarios operativos:** quien tenía acceso sin bypass a conteos, devoluciones, saldos o reportes de todas las sucursales deja de verlos hasta que se le asigne el bypass correspondiente. Los perfiles corporativos deben recibir estos permisos tras el despliegue.
- **Blobs:** la descarga del archivo asociado a una salida o a un vale pasa por `DocumentoSucursalScope` (`ValeBlobEndpoints.cs`, línea 126). No hay endpoint de descarga de packing list ni de evidencias de recepción. `PackingListBlobRef` y `ValeBlobRef` son referencias que envía el cliente y no se validan contra el blob (brecha, §16.7).

### 16.5 Cierre de mes

- **Endpoint y permisos:** `POST /api/v1/almacen/cierre-mes` exige `almacen.cierre-mes.ejecutar`. El handler exige además `almacen.cierre-mes.gestionar-todas-sucursales` (`VerificarSucursalAsync` sin sucursal), porque el cierre bloquea movimientos de todas las sucursales. Un usuario con solo `ejecutar` no puede cerrar hasta recibir el bypass.
- **Periodo concluido:** solo se cierran meses ya concluidos, medidos con el día local de negocio `America/Mexico_City` (`FechaNegocio`). El mes en curso o uno futuro responde 422 `CIERRE_PERIODO_FUTURO`. Un periodo ya cerrado responde 422 `PERIODO_YA_CERRADO`.
- **Pendientes:** responden 422 `CIERRE_CONTEOS_PENDIENTES`, `CIERRE_MOVIMIENTOS_PENDIENTES` o `CIERRE_VALES_PENDIENTES`, según el caso.
- **Respuesta:** `{ periodoId, anio, mes, cerradoAt }` (`EjecutarCierreMensualResponse`).
- **Alcance del periodo:** el periodo es de la empresa completa. No se notifica a Finanzas ni se regenera ALFAK al cerrar (brecha M-282, §16.7).

### 16.6 Inmutabilidad de recepción, salida y devolución interna (D17)

Las recepciones, las salidas y las devoluciones internas 8.A no tienen endpoints de edición ni de eliminación (`PUT`, `PATCH` o `DELETE`). Una corrección se registra como documento nuevo dentro de su propio flujo, por ejemplo una devolución 8.B o una regularización de vale. Esta decisión cubre M-118, M-149 y M-181.

### 16.7 Brechas documentadas (fuera de alcance de 86e3pc50v)

1. **Autorización de supervisor** para recibir fuera de límite (A5): no existe; rechazo duro.
2. **Tolerancia de cantidad** (por línea de OC o por material): retirada en 86e3tab1k; solo existe la tolerancia de dinero por proveedor (§10.7 del levantamiento).
3. **Backfill de `linea_oc_id` histórico** y herramienta de reproceso de eventos ya marcados como procesados (M-036). Ticket aparte.
4. **Validación de existencia y reutilización** de `PackingListBlobRef` y `ValeBlobRef` (M-109 y M-142). Ticket aparte. Las pruebas de integración vigentes registran referencias ficticias.
5. **Responsable del conteo:** se valida que el sub-almacén exista (P-30), pero no que el responsable exista (M-201, parcial). Requiere un puerto de empleados en el alta del conteo.
6. **Notificación a Finanzas y ALFAK automática al cerrar**, y reapertura de periodo en Almacén (M-282 y M-274).
7. **Valor a fecha de corte y N+1 de ALFAK**, y rótulo "Disponible" de MP-CNK (M-295, M-296 y M-300).
8. **Segregación de funciones** (solicitante distinto de autorizador, capturista distinto de aprobador) (M-167 y M-220).
9. **Lista y detalle propios de devoluciones internas 8.A** (M-181).
10. **Lock por OC, no por línea:** la consistencia del recibido entre Almacén y Compras es eventual (Compras lee el acumulado que calcula Almacén).
11. **Permiso `almacen.documentos.leer-todas-sucursales`:** lo usa `ResumenInicioQuery` de Almacén, pero no está definido en `PermisosCanonicos`. Revisar en otra tarea.

### 16.8 Cuarentena en recepción (ALM-04 G1.3)

Tarea 86e3jvu9j. Una recepción puede separar lo dañado: cada línea (variante A con factura y variante B con packing list) acepta `CantidadDanada` y `MotivoDano`. Lo bueno entra al rack disponible elegido y lo dañado entra a un rack real del sub-almacén `MaterialEnRevision` (MAT-REV) **del mismo almacén**, resuelto solo (primera ubicación activa, no default, ordenada por clave; sin ella la recepción responde 422 `RECEPCION_SIN_UBICACION_REVISION`).

**Modelo.** Un movimiento no puede tener líneas en dos sub-almacenes (lo impide el trigger), así que la recepción genera dos movimientos `EntradaCompra`: el principal y un hermano hacia MAT-REV con `RecepcionOrigenId` = recepción principal. Si todo está dañado solo existe el hermano. Se publica **un solo** `OcRecepcionRegistradaEvent`, con el contrato sin cambios, cuyas líneas incluyen las de ambos movimientos: la OC y la requisición ven el total recibido (supuesto V45: lo que está en revisión cuenta como recibido). Lo dañado se distingue en el payload por el `SubAlmacenId` de MAT-REV. Que el material en revisión **no sea pagable** queda para G1.4 (riesgo abierto: hasta entonces CxP puede tratarlo como pagable).

**Disponibilidad.** Las salidas (con requisición y vale) rechazan ubicaciones de MAT-REV y la disponibilidad que consumen Compras, apartados y reorden excluye MAT-REV: una recepción de 10 con 2 dañadas deja físico 10 y disponible 8.

**Dictamen.** Con el permiso `almacen.cuarentena.dictaminar` (lo arma el administrador en un rol de Gerente de almacén/calidad), cada línea en revisión se dictamina desde `/almacen/revision`:

- **Liberar** (total o parcial): un par de movimientos, la salida de MAT-REV de tipo 10 `SalidaPorLiberacionRevision` (folio `LIB`) y la entrada `ReincorporacionTrasRevision` al rack disponible asignado del mismo almacén.
- **Dar de baja:** movimiento `BajaPorDano` existente desde MAT-REV, con la regla vigente de esa baja (sin autorización adicional).
- **Rechazar al proveedor:** abre la `DevolucionAProveedor` existente (sub-flujo 8.B) y la pasa a `EnAutorizacion` en la misma transacción. Exige al menos una evidencia vigente de la línea (si no, 422 `DICTAMEN_RECHAZO_SIN_EVIDENCIA`). La evidencia se copia como `EvidenciaDevolucionProveedor` con `BlobRef = "adjunto:{adjuntoId}"`: es una referencia, nunca el blob real del servicio genérico (ADR-0058). Dirección autoriza y registra la salida desde MAT-REV; la devolución cruza con CxP solo por el `OcDevolucionRegistradaEvent` existente. Si Dirección la rechaza, el pendiente de la línea regresa.

Cada dictamen queda en la tabla `almacen.dictamenes_revision` (quién, cuándo, cantidad, motivo, resultado y movimientos o devolución vinculados), que sirve de bitácora y no se edita: un dictamen no se deshace, se corrige por contramovimiento. Dos dictámenes simultáneos sobre una línea se serializan con el mismo advisory lock de la devolución a proveedor. La evidencia por línea usa el servicio genérico de adjuntos G1.2 (`linea_revision`) y se sube desde la bandeja después de registrar la recepción. El alcance por sucursal reutiliza los permisos de bypass `almacen.entradas.{leer,gestionar}-todas-sucursales`.

**Plazo.** El dictamen tiene un plazo de 5 días hábiles (120 horas hábiles con el calendario compartido), definido como constante en código con nota `ponytail:`. Es el supuesto V45, que Millet aún no ratifica; el plano 03 pide que sea parámetro, así que se moverá a `AlmacenSettings` cuando se valide. La bandeja solo avisa «Vencido»; no hay notificación ni escalamiento automático.

**Reincorporación 8.A (arreglada en la rebanada 3).** `ReincorporacionTrasRevisionHandler` (sub-flujo 8.A, menú «Material en revisión: reincorporar» de Devoluciones) antes solo sumaba al destino y no descontaba MAT-REV, de modo que creaba existencia de la nada. Ahora usa el mismo par LIB/REI del dictamen (salida de MAT-REV más entrada al destino, misma cantidad); sin saldo suficiente en MAT-REV falla con `SALDO_INSUFICIENTE` y no crea existencia (y con `MATREV_RESERVADO_PARA_DICTAMEN` si lo que falta está reservado para dictamen).

**Lo reservado para dictamen no se consume por 8.A.** `POST /mat-rev/baja-por-dano` y `POST /mat-rev/reincorporar` piden solo `almacen.devoluciones-internas.capturar` (el permiso de 8.A no cambia), así que no podían ser un atajo para saltarse el dictamen. Los dos handlers calculan, bajo advisory lock por bin y dentro de la misma transacción que el movimiento, lo **reservado para dictamen** en el bin y artículo: la suma del pendiente de las líneas de recepción en revisión (en revisión − dictaminado − en devolución, la misma fórmula de la bandeja, extraída a `ReservaDictamen`). Si saldo del bin − reservado < cantidad pedida responden 422 `MATREV_RESERVADO_PARA_DICTAMEN`. El saldo 8.A ajeno a recepciones sí se consume. La reincorporación elige el bin de MAT-REV con más saldo libre (saldo − reservado). Ambos movimientos usan la unidad base del artículo, no `PZA` fija.

**`OcDevolucionRegistradaEvent.RecepcionOrigenId` en rechazos de cuarentena.** Una devolución abierta por un rechazo de dictamen no nace de la recepción principal sino de la línea en MAT-REV, así que su `RecepcionOrigenId` es el **id del movimiento hermano MAT-REV** (el movimiento `EntradaCompra` con `RecepcionOrigenId` hacia la recepción principal), no el de la recepción principal. El evento no cambia. No rompe a los consumidores porque ni Compras (decrementa `CantidadRecibida` por `LineaOcId` y `Cantidad` de cada línea del payload) ni CxP (genera la `NotaCargo` por `DevolucionId` y monto) correlacionan por `RecepcionOrigenId`; la idempotencia usa `devolucion_a_proveedor_id`. Quien necesite ligar la devolución con la recepción principal sigue el `RecepcionOrigenId` del movimiento hermano.

**Saldos y bandeja.** El listado de saldos (`ListarSaldos`) y el saldo por ubicación muestran `CantidadDisponible = 0` en filas de sub-almacenes MAT-REV: la existencia es física pero no surtible (la columna generada del saldo sigue igual a la cantidad; se corrige en la consulta). Si no hay calendario de días hábiles, la bandeja de revisión devuelve `PlazoNoCalculable = true` con `FechaLimite = null`, registra un warning en el log y la pantalla muestra «Plazo no calculable: falta el calendario de días hábiles».

**Limitaciones conocidas (ALM-04).**

- La existencia de materiales directos (sub-almacén `MaterialesDirectos`, reporte de existencia) **no muestra MAT-REV**: lo que está en revisión de una recepción de materiales directos solo se ve en la bandeja y en los saldos.
- En la variante B con factura pendiente, el movimiento hermano MAT-REV **no se vincula a la conciliación** cuando llega la factura (pendiente G1.4).
- P8.4a detiene las facturas nuevas de la OC aunque el daño ya se haya dictaminado (Q5): la recepción conserva la línea en MAT-REV y CxP no recibe un evento de dictamen; el área libera la factura a mano hasta que G1.4 traiga la elegibilidad parcial por línea. Lo fija la prueba `Factura_nueva_se_detiene_aun_si_el_dano_ya_se_dictamino_Q5`.

**Pendiente: liberación P8.4a / G1.4.** La liberación de material en revisión que reincorpora a disponible y la condición de «no pagable» en CxP (P8.4a del plano y G1.4) siguen pendientes; este diseño solo deja el par LIB/REI listo para ese flujo. Tampoco se da dictamen al material que llega a MAT-REV por devoluciones internas 8.A, que no tiene línea de recepción.

**Dependencias de plataforma pendientes (ADR-0031).**

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| `IAdjuntosEntidadReadPort` | `<AdjuntosEntidadReadAdapter>` | `NoOpAdjuntosEntidadReadPort` | `Api/Program.cs` registra `AdjuntosEntidadReadAdapter` de Compartido (lee solo id y nombre de los adjuntos vigentes; nunca `BlobRef`) |

**Contradicciones registradas (no resueltas aquí).**

- **C1 · Plazo.** Plano 03 «Dictamen de material observado»: «El calendario y escalamiento siguientes son parametrizables; no se inventa un plazo aprobado». Ficha (V45): «5 días hábiles». Se construye constante de 5 días hábiles con nota `ponytail:`; escalamiento y notificación quedan fuera de alcance.
- **C2 · Baja.** Plano 03, tabla de dictamen: «Dar de baja: evidencia de pérdida y autorización conforme al modelo de ajustes». Ficha §2: «baja por daño: con la regla existente de `BajaPorDano`» (sin autorización adicional). Se sigue la ficha.
- **C3 · Plano G1 vs código.** El plano G1 propone una ubicación `CUARENTENA`; la ficha lo corrige y se reutiliza `TipoSubAlmacen.MaterialEnRevision`. Se sigue la ficha.
- **C4 · Evidencias de Almacén.** `Api/Program.cs` mantiene `PLATFORM-TODO(<MigrarAdjuntosOcAlmacen>)` (evidencias de Almacén con patrón propio) y ADR-0058 manda los consumidores nuevos (recepciones) al servicio genérico. Esta tarea usa G1.2 para la recepción y deja la devolución legada con referencia `adjunto:{id}`.
- **C5 · Identificador.** La ficha local `Fichas/ALM-04.md` corresponde a otro paquete (reabasto y remanente) que reutiliza el código ALM-04.

## Rev.

- **2026-05-22 — v1.1** — Reservas de stock movidas a MVP (opción B del teardown de Compras). Agregada A19, agregado `ReservaStock` como agregado raíz, columna `cantidad_reservada` + tabla `reservas_stock` en §5, puerto público `IAlmacenReservaPort` en §6.2, eventos `StockReservadoEvent`/`StockLiberadoEvent` en §7.1, comandos en §8.1.
- **2026-05-22 — v1 (Draft)** — Propuesta de diseño inicial sobre el levantamiento v0.1.1. Asunciones A1-A18 listadas.
