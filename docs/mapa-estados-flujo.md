# Mapa de estados y flujo de movimientos

> Referencia visual de los state machines de cada agregado y cómo se
> conectan vía eventos cross-módulo en la triada
> **Compras ↔ Almacén ↔ CxP** (+ Tesorería futura).
>
> Validado contra el código en `2026-05-25`. Cuando algún enum o flujo
> cambie, actualizar este doc en el mismo PR.

## Índice

1. [Requisición (Compras)](#1-requisición-compras)
2. [Orden de Compra (Compras)](#2-orden-de-compra-compras)
3. [Movimiento de Inventario (Almacén)](#3-movimiento-de-inventario-almacén)
4. [Reserva de Stock (Almacén)](#4-reserva-de-stock-almacén)
5. [Factura Proveedor (CxP)](#5-factura-proveedor-cxp)
6. [Cadena cross-módulo](#6-cadena-cross-módulo-triada)
7. [Topología Service Bus](#7-topología-service-bus)
8. [Glosario terminológico](#8-glosario-terminológico)

---

## 1. Requisición (Compras)

`EstadoRequisicion` (backend/src/Compras/Domain/EstadoRequisicion.cs):

```
┌──────────┐  enviar    ┌────────────────┐  autorizar    ┌────────────┐
│ Borrador │ ─────────► │ EnAutorizacion │ ────────────► │ Autorizada │
└──────────┘            └────────────────┘               └────────────┘
                              │                                │
                              │ rechazar                       │ cubrimiento
                              ▼                                │ (almacén+OC)
                        ┌────────────┐                         ▼
                        │ Rechazada  │                  ┌───────────┐
                        └────────────┘                  │ EnSurtido │ ← cuando hay
                                                        └───────────┘   CantDeCompra > 0
                                                              │
                                                              │ todas las líneas
                                                              │ cubiertas (alm + recibido)
                                                              ▼
                                                        ┌───────────┐
                                                        │  Cerrada  │ ← (1) cubrimiento
                                                        └───────────┘   100% por almacén
                                                                       (skip EnSurtido)
                                                                     (2) recepción OC
                                                                       completa
```

**Atajos:**
- Cualquier estado activo → `Cancelada` (con motivo).
- `Borrador` → `Eliminada` (soft-delete).

**Nota crítica:** `Cerrada` ≠ "entregada al solicitante". Cuando la RQ
se cubre 100% por almacén transiciona directo a `Cerrada` **sin pasar
por `EnSurtido`**, pero las salidas físicas pueden seguir pendientes.
Por eso `RequisicionSelector` del sheet de salidas incluye `Cerrada`
en su filtro default.

---

## 2. Orden de Compra (Compras)

`EstadoOrdenCompra` (backend/src/Compras/Domain/Oc/EstadoOrdenCompra.cs):

```
┌──────────┐  enviar    ┌──────────────────────────┐  autoriza N1   ┌─────────────────────────┐
│ Borrador │ ─────────► │ EnAutorizacionJefeCompras│ ──────────────►│ EnAutorizacionDireccion │
└──────────┘            └──────────────────────────┘                └─────────────────────────┘
                                  │                                            │
                                  │ rechazar                                   │ autoriza N2
                                  ▼                                            ▼
                            ┌────────────┐                              ┌────────────┐
                            │ Rechazada  │                              │ Autorizada │
                            └────────────┘                              └────────────┘
                                                                              │
                                       3 sub-estados llegan a "Completa"      │
                                       ┌──────────────────────────────────────┘
                                       ▼
                                 ┌───────────┐
                                 │  Cerrada  │
                                 └───────────┘
```

### Sub-estados ortogonales en OC `Autorizada`

Las 3 dimensiones coexisten y avanzan independientemente:

| Dimensión | Estados | Quién avanza | Mecánica |
|---|---|---|---|
| `SubEstadoRecepcion` | `SinRecepcion` · `Parcial` · `Completa` | Almacén (worker PR #293/#299) | Suma de `LineaOC.CantidadRecibida` |
| `SubEstadoFacturacion` | `SinFactura` · `Parcial` · `Completa` | CxP (worker `CxpEventListenerWorker`) | Suma de `LineaOC.CantidadFacturada` |
| `SubEstadoPago` | `SinPago` · `Parcial` · `Pagada` | Tesorería (futuro) | Suma de pagos aplicados a facturas de la OC |

**Cierre automático:** Compras transiciona la OC a `Cerrada` cuando los
3 sub-estados son `Completa`/`Pagada`. Emite `OrdenCompraCerradaEvent`
una sola vez.

---

## 3. Movimiento de Inventario (Almacén)

### Estado del ciclo de vida

`EstadoMovimiento` (backend/src/Almacen/Domain/Movimientos/EstadoMovimiento.cs):

```
┌──────────┐  validar  ┌──────────┐  registrar  ┌────────────┐
│ Borrador │ ────────► │ Validado │ ──────────► │ Registrado │ ← impacta saldos
└──────────┘           └──────────┘             └────────────┘   (trigger PG en
                                                       │         misma TX)
                                                       │ (ya no se puede revertir
                                                       │  el row; sólo crear
                                                       │  movimiento inverso)
                                                       ▼
                                                ┌────────────┐
                                                │ Cancelado  │ ← compensatorio
                                                └────────────┘
```

### Tipo (qué representa el movimiento)

`TipoMovimiento` (backend/src/Almacen/Domain/Movimientos/TipoMovimiento.cs):

| Tipo | Descripción | Disparador típico | Vinculaciones |
|---|---|---|---|
| `EntradaCompra` | Recepción contra OC | "Nueva recepción" en Almacén | `OcId`, `OcLineaId`, opcional `FacturaId`/`CfdiRecibidoId` |
| `SalidaConsumo` | **Entrega** al solicitante con RQ | "Nueva salida variante A" | `RqId`, opcional `PersonaDestinatariaId`/`MaquinaDestinoId` |
| `SalidaPorVale` | Entrega urgente sin RQ (A14) | "Nueva salida variante B" | `ValeBlobRef`, opcional `RqRegularizadoraId` (post-hoc) |
| `DevolucionSalida` | Devolución interna que regresa al almacén (8.A) | "Devolución interna" | `SalidaOrigenId` |
| `SalidaPorDevolucionAProveedor` | Devolución física al proveedor (8.B) | "Devolución a proveedor" | `RecepcionOrigenId`, `ProveedorId`, `Motivo` |
| `AjustePositivo`/`AjusteNegativo` | Reconciliación tras inventario físico | Aprobación de conteo | `ConteoId` |
| `AjustePrecioFactura` | Recosteo retroactivo (variante B) | CxP detecta diferencia | `FacturaIdOrigenDiff` |
| `BajaPorDano` | Material en revisión que se desecha (A15) | Sub-flujo material en revisión | `Motivo`, `EstadoMaterial` |
| `ReincorporacionTrasRevision` | Material en revisión que se recupera | Sub-flujo material en revisión | `Motivo`, `EstadoMaterial` |

---

## 4. Reserva de Stock (Almacén)

`EstadoReserva` (backend/src/Almacen/Domain/Reservas/EstadoReserva.cs):

```
                  reservar (auto al              consumir = ocurre
                  autorizar RQ con               salida con RqId
                  CantDeAlmacen > 0)
                                              ┌──────────┐
                  ───────────────────────────►│ Consumida│
                                              └──────────┘
                  ┌────────┐
                  │ Activa │
                  └────────┘
                                              ┌──────────┐
                  ───────────────────────────►│ Liberada │
                                              └──────────┘
                  liberar (al cancelar RQ
                  o por TTL background job)
```

**Bandeja:** `/almacen/reservas` (default filtro: `Activa`).
Hooked via `IReservarStockPort` + `ILiberarReservaPort` (adapters
reales en PR #305).

---

## 5. Factura Proveedor (CxP)

`EstadoPasivo` (backend/src/CuentasPorPagar/Domain/FacturaProveedor/EstadoPasivo.cs):

```
┌───────────┐  enviar a   ┌────────────┐  autorizar    ┌────────────┐  pagar    ┌────────┐
│ Capturada │ ──────────► │ EnRevision │ ────────────► │ Autorizada │ ────────► │ Pagada │
└───────────┘             └────────────┘               └────────────┘           └────────┘
      │                         │                            │
      └─────── cancelar (cualquier estado pre-pago) ──────► ┌────────────┐
                                                            │ Cancelada  │
                                                            └────────────┘
```

---

## 6. Cadena cross-módulo (triada)

```
RQ Autorizada
  │  ─────► IReservarStockPort → reserva real en almacen.reservas_stock
  │         (PR #305 cableó el adapter productivo)
  │
  ▼
[Almacén] Salida con RQ (SalidaConsumo, Registrado)
  │  publica almacen.oc_recepcion.* (NO aplica)
  │  ─────► Compras in-proc: Requisicion.RegistrarRecepcion
  │         decrementa LineaRq.CantidadDeAlmacen — si todo cubierto: RQ → Cerrada


OC Autorizada
  │  publica compras.orden-compra.autorizada.v1
  │  ─────► CxP: espera factura (proyección)
  │  ─────► Almacén: acepta recepciones (validación IComprasOcReadPort)
  │
  ▼
[Almacén] Recepción (EntradaCompra, Registrado)
  │  publica almacen.oc_recepcion.registrada.v1 al topic almacen-events
  │  ─────► Compras (worker PR #293): ↑ LineaOC.CantidadRecibida
  │                                    → SubEstadoRecepcion
  │  ─────► CxP (worker F6-PR3): proyecta a recepciones_oc_local
  │
  ▼
[CxP] Factura registrada con OC (Capturada)
  │  publica cuentas_por_pagar.factura.registrada.v1
  │  ─────► Compras (worker PR D): ↑ LineaOC.CantidadFacturada
  │                                  → SubEstadoFacturacion
  │
  ▼
[Tesorería] Pago aplicado (FUTURO)
  │  publicará tesoreria.pago-factura-proveedor.aplicado.v1
  │  ─────► Compras: ↑ MontoPagado → SubEstadoPago = Pagada
  │  ─────► CxP: marca factura Pagada
  │
  ▼
Compras: 3 sub-estados Completa/Pagada → OC.Estado = Cerrada
  └─────► publica compras.orden-compra.cerrada.v1
           ─────► Almacén / Requisiciones (informativo: ya no aceptan
                  más movimientos contra esta OC)
```

### Devolución a proveedor (sub-flujo 8.B)

```
[Almacén] Devolución (SalidaPorDevolucionAProveedor)
  │  publica almacen.oc_devolucion.registrada.v1
  │  ─────► Compras: decrementa LineaOC.CantidadRecibida
  │  ─────► CxP: genera NotaCargo Borrador con datos de la devolución
  │
  ▼
[CxP] NotaCargo autorizada → publica cuentas_por_pagar.nota-cargo.autorizada.v1
                                ─────► (informativo en Compras)

[CxP] Nota Crédito fiscal del proveedor (relación CFDI tipo 03)
  │  publica cuentas_por_pagar.nota-credito-fiscal-devolucion.recibida.v1
  │  ─────► Almacén: cierra la devolución 8.B (ConciliadaConNcFiscal = true)
```

---

## 7. Topología Service Bus

```
                                    ┌──────────────────────────────┐
              ┌────────────────────►│ topic compras-events         │
              │                     │  ↳ cuentas-por-pagar-sub      │ → CxP ComprasEventListenerWorker
       publica│                     └──────────────────────────────┘
[Compras]─────┤
              │                     ┌──────────────────────────────┐
              │                     │ topic almacen-events         │ ← adoptado en Bicep PR #295
              │                     │  ↳ cuentas-por-pagar-sub      │ → CxP AlmacenEventListenerWorker
[Almacén]─────┼────────────────────►│  ↳ compras-subscription-alm.  │ → Compras AlmacenEventListenerWorker
              │  publica            └──────────────────────────────┘   (PR #293/#295)
              │
              │                     ┌──────────────────────────────┐
[CxP]─────────┼────────────────────►│ topic cuentas-por-pagar-events│
              │  publica            │  ↳ compras-subscription       │ → Compras CxpEventListenerWorker
              │                     └──────────────────────────────┘
              │
              │                     ┌──────────────────────────────┐
[Integ.A+W]───┴────────────────────►│ topic integraciones-aw-events│
                 publica            │  ↳ drop-subscription          │ → AwDropWorker
                                    └──────────────────────────────┘
```

**Convención de naming** (CLAUDE.md §"Triada"):
- Topic: `<modulo-en-kebab>-events`.
- Subscription: `<modulo-consumidor>-subscription` (o
  `<modulo>-subscription-<topic>` si el módulo ya tiene una sub en otro topic).
- EventType: `{Agregado}{Verbo}.v1` en camelCase + namespace por módulo.

**Wire format del payload:** PascalCase (default .NET — el
`OutboxSaveChangesInterceptor` no setea `PropertyNamingPolicy`).
Todos los consumers usan `PropertyNameCaseInsensitive = true` por
seguridad (cerrado en PRs #299 / #300).

---

## 8. Glosario terminológico

| Término | Acepción en el negocio |
|---|---|
| **Surtir** | Recibir mercancía del proveedor (vía OC + recepción). `EstadoRequisicion.EnSurtido` = "RQ esperando que llegue lo comprado". |
| **Entregar** | Salida física del almacén al solicitante (departamento, máquina, persona). Lo que hace `MovimientoInventario` tipo `SalidaConsumo`/`SalidaPorVale`. |
| **Cubrimiento** | Cómo se cubrirá cada línea de RQ: parte con almacén (`CantDeAlmacen`), parte con compra (`CantDeCompra`). Decidido al autorizar la RQ. |
| **Recepción** | Movimiento `EntradaCompra` en Almacén contra una OC. |
| **Salida (variante A)** | `SalidaConsumo` con `RqId` — entrega normal contra RQ aprobada. |
| **Vale urgente (variante B)** | `SalidaPorVale` sin RQ origen — entrega de emergencia que se regulariza en 48 h vinculando una RQ aprobada (A14). |
| **Reserva** | Comprometer cantidad disponible de almacén contra una RQ aprobada. No decrementa stock físico, sí decrementa `CantidadDisponible` de saldos. |
| **Liberar reserva** | Devolver la cantidad reservada al disponible (cancelación de RQ o TTL). |
| **Consumir reserva** | Al hacer la salida con RQ, la reserva pasa a `Consumida` y el saldo físico decrementa. |
| **NotaCargo** | Documento en CxP que reclama crédito al proveedor por devolución. Espera la NC fiscal (CFDI tipo 03) para cierre contable. |

---

## Validaciones útiles

- **Buscar PLATFORM-TODO restantes:** `rg "PLATFORM-TODO" backend/src`.
- **Buscar quién emite un EventType:** `rg "<event-type-string>.v1" backend/src`.
- **Buscar quién consume un EventType:** look at workers en
  `backend/src/*/Infrastructure/Workers/`.
- **Estados pendientes de implementación:** comparar con
  `docs/modulos/<modulo>/00-levantamiento.md` y
  `docs/modulos/<modulo>/01-diseno.md`.
