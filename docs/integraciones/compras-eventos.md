# Eventos de integración del módulo Compras

> Versión inicial del contrato de eventos publicados por el módulo
> **Compras / Requisiciones** al broker de integración (Azure Service
> Bus, ADR-0009). Cada consumer fuera del módulo (BI, Notificaciones,
> Reportes, sistemas externos) consume estos eventos sin acoplamiento
> directo a la base de datos de Compras.
>
> **Versión:** v1 (cierre de Fase 6, F6-PR4).
> **Fecha:** 2026-05-08.
>
> Cuando un evento cambia su shape de manera no compatible (campos
> renombrados, eliminados, tipos cambiados), se incrementa la versión
> (`v2`, `v3`...) y ambas conviven hasta que todos los consumers
> migren. Cambios aditivos (campos nuevos opcionales) NO requieren
> bump de versión.

---

## Cómo se publican

Cada flujo de negocio que termina en una transición relevante emite
un domain event. Un mapper in-proc (`Application/Integration/Mappers/`)
traduce el domain event al integration event correspondiente y lo
publica vía `IIntegrationEventPublisher`. El publisher encolas al
buffer scoped; el `OutboxSaveChangesInterceptor` persiste cada evento
como una fila en `compras.integration_events_outbox` dentro de la
**misma transacción EF** que la operación de negocio (atomicidad
ADR-0009).

El `OutboxPublisherWorker` (background service) polea filas con
`published_at IS NULL`, las publica al tópico configurado de Service
Bus (default `compras-events`), y marca `published_at`. Filas con
`attempts > MaxAttempts` quedan en outbox como dead-letter pasivo
para inspección manual.

**Headers presentes en cada mensaje de Service Bus:**

| Header | Tipo | Descripción |
|---|---|---|
| `Subject` (Service Bus) | string | Igual a `EventType` (ej. `compras.requisicion.autorizada.v1`) |
| `MessageId` | string | UUID v7 — id estable del evento (idempotencia consumer) |
| `ContentType` | string | `application/json` |
| `EventType` (application property) | string | Discriminador para enrutamiento por suscripción |
| `EmpresaId` (application property) | string (Guid) | Multi-tenant routing |
| `OccurredAt` (application property) | string ISO-8601 | Timestamp del evento (no del envío) |

El cuerpo (`Body`) es JSON serializado del integration event concreto.
Todos los campos del payload llevan `PascalCase` (convención .NET
default; consumers deserializan a su tipo).

---

## Convención de nombres

`<modulo>.<recurso>.<acción>.v<N>`

- **modulo**: `compras` (estable; los demás módulos seguirán el mismo patrón cuando integren).
- **recurso**: `requisicion`.
- **acción**: estado o evento puntual (`autorizada`, `rechazada`, `eliminada`, `cancelada`, `cerrada`, `saldoNoSurtido`).
- **versión**: `v1`, `v2`, ...

---

## Garantías

- **At-least-once delivery**: el outbox + retry exponencial garantizan
  que cada evento se publica al broker eventualmente. Consumers deben
  tolerar duplicados (usar `MessageId` para deduplicar).
- **Atomicidad con la operación de negocio**: si SaveChanges del
  comando rollbackea, los eventos no se publican (la fila tampoco se
  inserta en el outbox).
- **Sin orden global**: los eventos de la misma `RequisicionId` siguen
  el orden temporal (`OccurredAt` ASC) del polling FIFO; entre RQs no
  hay orden garantizado.
- **No se transmiten campos sensibles**: el shape mínimo solo lleva
  IDs + audit + motivo cuando aplica. Consumers que necesiten datos
  como Folio, líneas, montos, etc. deben consultar Compras vía API
  REST (`GET /api/v1/compras/requisiciones/{id}`).

---

## `compras.requisicion.autorizada.v1`

**Cuándo se emite:** cuando una requisición cumple la matriz de
aprobación (todas las autorizaciones requeridas registradas) y
transiciona a `Autorizada` (incluso si la bifurcación stock-aware la
lleva inmediatamente a `Cerrada` o `EnSurtido`; el momento de
aprobación es lo que se reporta).

**Origen:** `Requisicion.RegistrarAutorizacion` cuando satisface
matriz, vía `MatrizAprobacionSatisfechaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.requisicion.autorizada.v1` |
| `EmpresaId` | Guid | Multi-tenant |
| `OcurridoEn` | datetime ISO-8601 | Timestamp del evento |
| `RequisicionId` | Guid | Id de la requisición autorizada |

**Ejemplo:**

```json
{
  "EventType": "compras.requisicion.autorizada.v1",
  "EmpresaId": "0a1b2c3d-...",
  "OcurridoEn": "2026-05-08T14:30:00.000Z",
  "RequisicionId": "0193a8c1-..."
}
```

---

## `compras.requisicion.rechazada.v1`

**Cuándo se emite:** cuando un autorizador rechaza una requisición
desde `EnAutorizacion` → `Rechazada` (terminal).

**Origen:** `Requisicion.Rechazar`, vía `RequisicionRechazadaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.requisicion.rechazada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `RequisicionId` | Guid | |
| `MotivoId` | Guid | FK a `compras.motivos_rechazo` |
| `MotivoTexto` | string?? (nullable) | Texto libre cuando el motivo lo requiere (`permite_texto_libre = true`) |
| `ActorId` | Guid | Usuario que rechazó |

**Ejemplo:**

```json
{
  "EventType": "compras.requisicion.rechazada.v1",
  "EmpresaId": "0a1b2c3d-...",
  "OcurridoEn": "2026-05-08T14:35:00.000Z",
  "RequisicionId": "0193a8c1-...",
  "MotivoId": "00000003-0002-0000-0000-000000000001",
  "MotivoTexto": null,
  "ActorId": "0193a8c2-..."
}
```

---

## `compras.requisicion.eliminada.v1`

**Cuándo se emite:** cuando una requisición se elimina pre-autorización
(estado `Borrador` o `EnAutorizacion`) → `Eliminada` (terminal).
**No** toca `DeletedAt`: el estado terminal es la marca, las RQs
siguen visibles en bandejas (filtradas por estado).

**Origen:** `Requisicion.Eliminar`, vía `RequisicionEliminadaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.requisicion.eliminada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `RequisicionId` | Guid | |
| `MotivoId` | Guid | |
| `MotivoTexto` | string? (nullable) | |
| `ActorId` | Guid | Usuario que eliminó |

**Ejemplo:**

```json
{
  "EventType": "compras.requisicion.eliminada.v1",
  "EmpresaId": "0a1b2c3d-...",
  "OcurridoEn": "2026-05-08T14:40:00.000Z",
  "RequisicionId": "0193a8c1-...",
  "MotivoId": "00000003-0002-0000-0000-000000000003",
  "MotivoTexto": "Duplicada — se reemplaza por RQ MID2026-000123",
  "ActorId": "0193a8c2-..."
}
```

---

## `compras.requisicion.cancelada.v1`

**Cuándo se emite:** cuando una requisición autorizada (`Autorizada`
o `EnSurtido`) se cancela → `Cancelada` (terminal). En el mismo flujo
el handler libera reservas en Almacén (`ILiberarReservaPort`) y aborta
la OC borrador asociada.

**Origen:** `Requisicion.Cancelar`, vía `RequisicionCanceladaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.requisicion.cancelada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `RequisicionId` | Guid | |
| `MotivoId` | Guid | |
| `MotivoTexto` | string? (nullable) | |
| `ActorId` | Guid | Usuario que canceló |

**Ejemplo:** análogo a `rechazada.v1` con `EventType` distinto.

---

## `compras.requisicion.cerrada.v1`

**Cuándo se emite:** cuando todas las líneas tienen
`CantidadPendiente == 0` y la RQ transiciona a `Cerrada`. Hay dos
paths que disparan el cierre:

1. **Stock total** (F4-PR1): la bifurcación stock-aware cubre todas
   las líneas con `CantidadDeAlmacen` y deja `CantidadDeCompra=0` →
   transición directa `Autorizada → Cerrada`.
2. **Recepción de OC** (F5-PR1): la última recepción de material
   cierra la última línea pendiente → `EnSurtido → Cerrada`.

Los dos paths emiten el mismo integration event; los consumers no
necesitan distinguir el origen.

**Origen:** `Requisicion.RegistrarCubrimiento` o
`Requisicion.RegistrarRecepcion`, vía `RequisicionCerradaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.requisicion.cerrada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `RequisicionId` | Guid | |

---

## `compras.requisicion.saldoNoSurtido.v1`

**Cuándo se emite:** cuando una OC ligada a la requisición se cierra
entregando menos de lo solicitado. **Informativo** (asunción A12 del
diseño): no abre re-autorización, solo se reporta.

**Origen:** `OcCerradaEvent` (puerto OC) → `OcCerradaListener` detecta
saldo → publica `SaldoNoSurtidoEvent` → mapper.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.requisicion.saldoNoSurtido.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `RequisicionId` | Guid | |
| `LineaRequisicionId` | Guid | Línea afectada |
| `OrdenCompraId` | Guid | OC que se cerró con saldo |
| `CantidadSolicitada` | decimal | Lo que la línea pidió a OC |
| `CantidadEntregada` | decimal | Lo que OC efectivamente entregó |
| `Saldo` | decimal | Diferencia (siempre > 0; si es 0 el evento no se emite) |

**Ejemplo:**

```json
{
  "EventType": "compras.requisicion.saldoNoSurtido.v1",
  "EmpresaId": "0a1b2c3d-...",
  "OcurridoEn": "2026-05-08T14:50:00.000Z",
  "RequisicionId": "0193a8c1-...",
  "LineaRequisicionId": "0193a8c3-...",
  "OrdenCompraId": "0193a8c4-...",
  "CantidadSolicitada": 100.0,
  "CantidadEntregada": 70.0,
  "Saldo": 30.0
}
```

---

## Eventos del submódulo Órdenes de Compra (F8-PR1)

A continuación los 6 eventos del submódulo OC. Convención de nombres
(`compras.orden-compra.<acción>.v1`), entrega y headers son idénticos
a los eventos de RQ (sección anterior).

### `compras.orden-compra.enviada-a-autorizacion.v1`

**Cuándo se emite:** OC transmitida del Borrador (o Rechazada) a
EnAutorización Jefe Compras (§5.3 del 01-diseño).

**Origen:** `OrdenCompra.EnviarAAutorizacion` →
`OrdenCompraEnviadaAAutorizacionEvent` → `OcEnviadaAAutorizacionMapper`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.orden-compra.enviada-a-autorizacion.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `OrdenCompraId` | Guid | |
| `Folio` | string | Ej. `OC-MID2026-000001` |
| `CompradorTitularId` | Guid | Para enrutar notificación al comprador |

### `compras.orden-compra.autorizada.v1`

**Cuándo se emite:** Autorización N2 (Dirección) exitosa →
transición a `Autorizada`. Inicia ciclo de recepción/facturación/pago.

**Origen:** `OrdenCompra.Autorizar(Nivel2)` → `OrdenCompraAutorizadaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.orden-compra.autorizada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `OrdenCompraId` | Guid | |
| `Folio` | string | |
| `CompradorTitularId` | Guid | |
| `FechaContabilizacion` | datetime ISO-8601 | Misma fecha que `OcurridoEn` en V1; reservado para futura distinción |

### `compras.orden-compra.rechazada.v1`

**Cuándo se emite:** Rechazo en N1 o N2 → transición a `Rechazada`.
El nivel se infiere del estado previo.

**Origen:** `OrdenCompra.Rechazar` → `OrdenCompraRechazadaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.orden-compra.rechazada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `OrdenCompraId` | Guid | |
| `Folio` | string | |
| `CompradorTitularId` | Guid | |
| `UsuarioRechazadorId` | Guid | Quién rechazó (para audit y plantilla) |
| `MotivoRechazoId` | Guid | FK a catálogo `compras.motivos_rechazo` |
| `MotivoRechazoTexto` | string? | Texto libre si el motivo permite |

### `compras.orden-compra.cancelada.v1`

**Cuándo se emite:** Cancelación de OC (sin recepciones — F3-PR3 —
o con recepciones parciales — F5-PR4). V1 no distingue el path;
los consumers que necesiten ese detalle consultan vía API.

**Origen:** `OrdenCompra.Cancelar` o `CancelarConRecepcionesParciales`
→ `OrdenCompraCanceladaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.orden-compra.cancelada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `OrdenCompraId` | Guid | |
| `Folio` | string | |
| `CompradorTitularId` | Guid | |
| `UsuarioCanceladorId` | Guid | |
| `MotivoCancelacionId` | Guid | |
| `MotivoCancelacionTexto` | string? | |

### `compras.orden-compra.cerrada.v1`

**Cuándo se emite:** Cierre automático cuando las 3 dimensiones
sub-estado cumplen criterio de cierre simultáneamente
(SubRecepcion=Completa + SubFacturacion=Completa + SubPago=Pagada)
y `Estado=Autorizada`. Disparado por F5-PR1 al recalcular sub-estados.

**Origen:** `OrdenCompra.Registrar{Recepcion,Facturacion,Pago}` cuando
todas las dimensiones cierran → `OrdenCompraCerradaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.orden-compra.cerrada.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `OrdenCompraId` | Guid | |
| `Folio` | string | |
| `CompradorTitularId` | Guid | |

### `compras.orden-compra.reabierta.v1`

**Cuándo se emite:** Una OC `Cerrada` vuelve a `Autorizada` porque una
devolución o nota de crédito dejó alguna dimensión fuera de cierre
(F5-PR2).

**Origen:** Mismas operaciones que `.cerrada.v1`, ruta inversa →
`OrdenCompraReabriertaEvent`.

| Campo | Tipo | Descripción |
|---|---|---|
| `EventType` | string | `compras.orden-compra.reabierta.v1` |
| `EmpresaId` | Guid | |
| `OcurridoEn` | datetime ISO-8601 | |
| `OrdenCompraId` | Guid | |
| `Folio` | string | |
| `CompradorTitularId` | Guid | |

---

## Cambios respecto a versiones previas

### v1 OC — F8-PR1 (2026-05-12)

Adición de los 6 eventos del submódulo Órdenes de Compra. Misma
infraestructura outbox que los eventos de RQ; los mappers se
autodescubren por scanning de MediatR.

### v1 inicial — F6-PR4 (2026-05-08)

Versión inicial. 6 eventos publicados por Compras al cierre de Fase 6.
PLATFORM-TODO `<Outbox>` cerrado: el `IIntegrationEventPublisher`
real (basado en outbox transaccional + worker Service Bus) reemplazó
al `NoOpIntegrationEventPublisher`.
