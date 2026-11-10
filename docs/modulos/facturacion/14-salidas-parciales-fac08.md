# Salidas parciales facturables (FAC-08) — Adenda al módulo Facturación

> **Proyecto:** ERP Millet — Módulo 3 del back-office (Facturación CFDI 4.0).
> **Tarea:** FAC-08 (`86e3qdddf`, Fase 2 `86e3yt7r7`) · **Versión:** 1.1 · **Fecha:** 2026-11-07
> **Estado:** Fase 1 (fuente Comercio Exterior) y Fase 2 (fuente Producto terminado) implementadas.
>
> **Documentos relacionados:** [`01-diseno.md`](01-diseno.md) (§8 sin estado parcial, D19/D20),
> [`docs/modulos/producto-terminado/01-diseno.md`](../producto-terminado/01-diseno.md) (PT5.5, Fase 2).

---

## 1. Qué resuelve

Un pedido de A+W puede salir en partes: varios contenedores de exportación o varias salidas
de Producto terminado. Cada salida se factura sola y el pedido original no se modifica
(junta del 12-oct). Criterio de la ficha: **no se facturan 100 piezas en cada contenedor**.
Un pedido de 100 piezas repartido en dos contenedores (60 y 40) da **dos salidas y dos facturas**.

- `SalidaFacturable` guarda número de parcialidad, origen, referencia, líneas `{posición, cantidad, importe}`
  y total. La suma de las salidas vigentes nunca rebasa lo pedido.
- La factura de una salida se liga con `FacturaVenta.SalidaFacturableId` y no marca el pedido como facturado.
- Si A+W modifica un pedido con salidas vigentes, la modificación va a la bandeja de excepciones
  (`ModificacionSobreFacturado`).
- El registro manual (`POST /api/v1/facturacion/pedidos-facturables/{id}/salidas`, PR #354) sigue como respaldo.

## 2. Fuentes de salidas

| Fuente | Cómo llega | Estado |
|---|---|---|
| Contenedor embarcado (Comercio Exterior) | Evento `comercio_exterior.contenedor.embarcado.v1` → `ComercioExteriorEventListenerWorker` de Facturación | **Fase 1, conectada** |
| Salida con albarán (Producto terminado, PT5.5) | Puerto `IFacturacionSalidaPort` al emitir la hoja de salida (§6.1) | **Fase 2, conectada** |
| Manual | API / pantalla «Salidas parciales» | Respaldo |

## 3. Contrato `comercio_exterior.contenedor.embarcado.v1`

Lo publica Comercio Exterior por Outbox (ADR-0009) en el topic `comercio-exterior-events`, en la misma
transacción que registra la fecha real de embarque del contenedor (`MutarContenedorCommand`, acción
`embarcar`). Record: `ContenedorEmbarcadoEvent` (`backend/src/ComercioExterior/Application/ContenedorEventos.cs`).

| Campo | Tipo | Nota |
|---|---|---|
| `EmpresaId` | uuid | Empresa del expediente |
| `ExpedienteId`, `FolioExpediente` | uuid, texto | Expediente de exportación |
| `ContenedorId` | uuid | Llave de idempotencia junto con el pedido |
| `NumeroContenedor` | texto | Va como `Referencia` de la salida |
| `BlGuia` | texto | BL o guía |
| `FechaEmbarqueReal` | fecha | Fecha real capturada en CE |
| `Pedidos[]` | lista | Un elemento por pedido del contenedor |
| `Pedidos[].PedidoFacturableId` | uuid | Id del `PedidoFacturable` de Facturación (CE lo guarda en `PartidaEmbarcada`) |
| `Pedidos[].NumeroPedido` | texto | Número de pedido A+W (informativo; CE lo lee de Facturación) |
| `Pedidos[].Lineas[]` | `{Posicion, Cantidad, Unidad}` | Suma de las partidas vigentes del contenedor por posición |

Solo entran las partidas no liberadas del contenedor. Varias partidas de la misma posición en un
contenedor se suman en una línea.

## 4. Contrato `comercio_exterior.contenedor.cancelado.v1`

Se publica cuando se cancela un contenedor **que ya estaba embarcado** (antes del embarque no hubo
salida que corregir). Record: `ContenedorCanceladoEvent`. Campos: `EmpresaId`, `ExpedienteId`,
`FolioExpediente`, `ContenedorId`, `NumeroContenedor`, `Motivo` y `Pedidos[]` (mismo formato).

CE sigue impidiendo cancelar un contenedor cuyo pedido tiene factura vigente **del pedido completo**.
Una factura de salida no marca el pedido como facturado, por eso CE sí permite cancelar el contenedor y
Facturación decide.

## 5. Consumo en Facturación

Worker `ComercioExteriorEventListenerWorker` (`backend/src/Facturacion/Infrastructure/Workers/`):
suscripción `facturacion-ce-sub` (filtro `EventType IN (embarcado, cancelado)`, Bicep en
`infra/modules/servicebus.bicep`) y consumidor de entrega local C1.8. Procesa un mensaje a la vez.

**Embarcado** (`RegistrarSalidasContenedorHandler`): por cada pedido envía el mismo
`RegistrarSalidaFacturableCommand` del registro manual, con `Origen = ContenedorExportacion`,
`Referencia = NumeroContenedor` y `OrigenId = ContenedorId`.

- Idempotencia por `(empresa, origen, contenedor, pedido)`: índice único parcial
  `ux_salida_facturable_origen_pedido` (`origen_id IS NOT NULL`). Una reentrega regresa la salida ya
  registrada sin cambiar importes. Las salidas sin `origen_id` (manual, PT) siguen únicas por referencia
  (`ux_salida_facturable_referencia`, ahora parcial `origen_id IS NULL`).
- Un pedido que no admite la salida (no existe, ya está facturado completo, rebasa lo pendiente) abre una
  excepción `Otro` en la bandeja y no detiene a los demás pedidos del contenedor.
- Un pedido bloqueado (facturándose) hace que el evento se reintente.

**Cancelado** (`CancelarSalidasContenedorHandler`), idempotente por contenedor (`evento_procesado`):

- Salida sin factura vigente → se **descarta** (`descartada_en`, `motivo_descarte`). Se conserva para el
  histórico, ya no se factura (`SALIDA_DESCARTADA`) y sus cantidades vuelven a quedar pendientes.
  Una reentrega tardía del embarque no la revive.
- Salida con factura vigente → se queda como está y se abre una excepción `CancelacionSobreFacturado`
  con contenedor, parcialidad y folio, para decidir cancelación o sustitución del CFDI (la factura manda, D20).

## 6. Fase 2 · Salida con albarán de Producto terminado

### 6.1 Contrato: puerto, no evento

El plan proponía un evento `producto_terminado.salida.registrada.v1`. **No se crea:** desde PT5.5 (#433) Producto
terminado ya entrega la salida a Facturación por el puerto `IFacturacionSalidaPort`
(`backend/src/ProductoTerminado/Application/Ports/`), que Facturación implementa en
`FacturacionSalidaAdapter` (`Infrastructure/PublicAdapters`). Un evento duplicaría ese camino.

| Operación | Cuándo la llama PT | Qué hace Facturación |
|---|---|---|
| `RegistrarSalidaAsync(pedidoFacturableId, folioAlbarán, líneas {posición, cantidad})` | Al emitir la hoja de salida (`EmitirHojaSalidaHandler`), una vez por albarán con piezas; un albarán es de un solo pedido | `RegistrarSalidaFacturableCommand` con `Origen = ProductoTerminado`, `Referencia = folio del albarán`, sin `OrigenId`. Idempotente por `(empresa, origen, referencia)` (`ux_salida_facturable_referencia`): reenviar el mismo albarán regresa la salida ya registrada; con otras cantidades, `SALIDA_REFERENCIA_DISTINTA`. Rebasar lo pedido falla y la emisión no se completa |
| `CancelarSalidaAsync(folioAlbarán, motivo)` | **Aún no:** PT solo cancela hojas en `Borrador` (PT S23). Queda listo para cuando PT cancele una hoja emitida (PT5.8) | `CancelarSalidaProductoTerminadoCommand`: misma regla que el contenedor cancelado (§5, `CancelacionSalidas`). Sin factura vigente, la salida se descarta y lo embarcado vuelve a quedar pendiente. Con factura vigente, se conserva y se abre `CancelacionSobreFacturado` con el folio del albarán y el de la factura. Repetirla no cambia nada. Sin salida con ese folio, no hace nada |

Ejemplo: un pedido de 100 piezas que sale en dos rutas (60 y 40) da dos albaranes, dos salidas y dos facturas.

### 6.2 Retiro del stub

Se retiran `ISalidasParcialesReader`, `StubSalidasParcialesReader` y `SalidasParcialesWorker`
(`PLATFORM-TODO(<SalidasPt>)`) y la configuración `Facturacion:Workers:SalidasParciales`: ningún origen
los usa (PT entra por el puerto y CE por evento). El registro manual sigue como respaldo.

## 7. Compuerta de facturación por contenedor

La factura de un pedido ligado a un expediente exige el 100 % del pedido embarcado. Para una salida con
`Origen = ContenedorExportacion`, Facturación pide la compuerta de **ese contenedor**
(`IEmbarqueExportacionReadPort.ObtenerAsync(pedido, contenedorId)`): basta con que esté activo, embarcado y
con partidas del pedido. El CCE y el PDF llevan solo ese contenedor. Sin salida, la regla del 100 % no cambia.

## 8. Supuestos a validar

- Con el dueño de Comercio Exterior: el evento nuevo, la compuerta por contenedor y que la cancelación de un
  contenedor embarcado con salida facturada se permita en CE y se revise en Facturación.
- Con Millet: numeración real de parcialidades en A+W (sigue como supuesto de FAC-08).
- Con el dueño de Producto terminado: que la cancelación de una hoja emitida (PT5.8) llame `CancelarSalidaAsync` por albarán.
