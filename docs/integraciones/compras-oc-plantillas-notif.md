# Plantillas de notificación — Órdenes de Compra

> Plantillas de email coordinadas con el módulo **Notificaciones**
> (cuando exista). Mientras Notificaciones no esté en el monorepo,
> este doc queda como **contrato preliminar** entre Compras y el
> módulo consumidor. Los plantillas reales se materializan en código
> cuando Notificaciones haga su F1.
>
> **Versión:** v1 preliminar (F8-PR2).
> **Fecha:** 2026-05-12.

---

## Mapeo evento → plantilla

| Integration event | Plantilla | Destinatario(s) | Disparo |
|---|---|---|---|
| `compras.orden-compra.enviada-a-autorizacion.v1` | `oc-pendiente-autorizacion-n1` | Autorizadores N1 (Jefe Compras) | OC pasa de Borrador a EnAutorización N1 |
| `compras.orden-compra.enviada-a-autorizacion.v1`* | `oc-pendiente-autorizacion-n2` | Autorizadores N2 (Dirección) | Tras autorización N1 exitosa, cuando OC pasa a EnAutorización Dirección |
| `compras.orden-compra.autorizada.v1` | `oc-autorizada-al-comprador` | `CompradorTitularId` | Notifica al comprador que su OC quedó lista |
| `compras.orden-compra.rechazada.v1` | `oc-rechazada-con-motivo` | `CompradorTitularId` | Email al comprador con motivo + texto |
| `compras.orden-compra.cancelada.v1` | `oc-cancelada-al-comprador` | `CompradorTitularId` | Notifica cancelación al comprador |
| `compras.orden-compra.cerrada.v1` | (sin notif — solo BI) | — | Histórico/BI, no email |
| `compras.orden-compra.reabierta.v1` | (sin notif — solo BI) | — | Histórico/BI, no email |

\* El evento `enviada-a-autorizacion.v1` se emite también al transicionar
a N2 (autorización N1 → EnAutorizacion Dirección). El módulo
Notificaciones discrimina por el estado actual de la OC al recibir el
evento (consulta vía API si necesita) y rendera la plantilla N1 o N2.
Alternativa: agregar un campo `Nivel` al evento en v2.

---

## Plantilla: `oc-pendiente-autorizacion-n1`

**Asunto:** `[Millet] OC {Folio} pendiente de autorización — Jefe Compras`

**Cuerpo (placeholder):**

```
Hola {NombreJefeCompras},

La OC {Folio} fue transmitida por {NombreComprador} y está esperando
tu autorización (Nivel 1).

Total estimado: {Moneda} {TotalAPagar}
Proveedor: {NombreProveedor}
Fecha documento: {FechaDocumento}

Revisar y autorizar: {LinkOC}

— Sistema Millet ERP
```

**Variables que se obtienen vía API REST** (consumer las consulta tras
recibir el evento):
- `NombreJefeCompras` ← `GET /api/v1/identidad/usuarios/{autorizadorId}`
- `NombreComprador` ← idem con `CompradorTitularId`
- `TotalAPagar`, `Moneda`, `NombreProveedor`, `FechaDocumento` ← `GET /api/v1/compras/ordenes/{id}`
- `LinkOC`: URL del front (`https://erp.millet.mx/compras/ordenes/{id}`)

---

## Plantilla: `oc-pendiente-autorizacion-n2`

Idéntica a N1 pero con asunto y cuerpo apuntando a Dirección. Variable
extra: `AutorizadorN1` (quién ya firmó), obtenida vía API
`GET /api/v1/compras/ordenes/{id}` → último elemento en
`autorizaciones` con `Nivel=Nivel1`.

---

## Plantilla: `oc-autorizada-al-comprador`

**Asunto:** `[Millet] Tu OC {Folio} fue autorizada — lista para enviar al proveedor`

**Cuerpo:**

```
Hola {NombreComprador},

Tu OC {Folio} pasó las 2 autorizaciones y quedó en estado AUTORIZADA.

Total: {Moneda} {TotalAPagar}
Proveedor: {NombreProveedor}
PDF descargable: {LinkPdf}

— Sistema Millet ERP
```

---

## Plantilla: `oc-rechazada-con-motivo`

**Asunto:** `[Millet] OC {Folio} rechazada — {MotivoClave}`

**Cuerpo:**

```
Hola {NombreComprador},

Tu OC {Folio} fue rechazada por {NombreRechazador} (Nivel {NivelRechazo}).

Motivo: {MotivoNombre}
Detalle: {MotivoRechazoTexto}

La OC volvió a Borrador para que puedas corregirla.

Editar: {LinkOC}

— Sistema Millet ERP
```

---

## Plantilla: `oc-cancelada-al-comprador`

**Asunto:** `[Millet] OC {Folio} cancelada`

**Cuerpo:**

```
Hola {NombreComprador},

La OC {Folio} fue cancelada por {NombreCancelador}.

Motivo: {MotivoNombre}
Detalle: {MotivoCancelacionTexto}

— Sistema Millet ERP
```

---

## Estado de implementación

El módulo Notificaciones **no existe todavía** en el monorepo. Este
documento queda como contrato preliminar y se materializa en código
cuando:

1. Notificaciones publique su F1 (`AddNotificaciones` extension method
   en SharedKernel o módulo dedicado).
2. Notificaciones suscriba al topic de Service Bus configurado en
   `Compras.Outbox.ServiceBusTopicName` (default `compras-events`).
3. Notificaciones implemente plantillas con `Razor.Templating` o
   similar (decisión específica de ese módulo).

Mientras tanto, los integration events ya salen al Service Bus
(verificable con un consumer dummy), por lo que la integración futura
no requiere cambios en Compras — solo wiring del consumer.

---

## Cambios respecto a versiones previas

### v1 preliminar — F8-PR2 (2026-05-12)

Versión inicial. Define 5 plantillas (3 para flujo de autorización +
cancelación + rechazo) ligadas a los 6 integration events de OC.
Cerrada/Reabierta no generan email — son solo histórico/BI.
