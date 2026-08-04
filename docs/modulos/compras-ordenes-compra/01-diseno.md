# Diseño — Submódulo Órdenes de Compra (Módulo Compras)

> **Construido sobre:** [00-levantamiento-mapa-funcional.md](00-levantamiento-mapa-funcional.md)
> (mapa funcional validado con Rodrigo Chay y Eduardo Paredes el 2026-05-11).
>
> **Hereda contexto de:** [`docs/modulos/compras-requisiciones/01-diseno.md`](../compras-requisiciones/01-diseno.md)
> (Rev. 14) — el submódulo de Requisiciones ya implementado define los
> puertos `IGenerarSolicitudCompraPort`, `OcRecepcionRegistradaEvent` y
> `OcCerradaEvent` que este submódulo debe satisfacer.
>
> **Estado:** propuesta de diseño v1 para revisión con el owner. Las
> decisiones marcadas como `[Asunción]` requieren confirmación del
> cliente antes de implementar; están listadas en §3.
>
> **Fecha:** 2026-05-11.

---

## 0. Cómo leer este documento

- `[Decidido]` — fijado por ADR existente, por el mapa funcional §10 ya
  cerrado, o por sesión con el cliente.
- `[Asunción]` — propuesta del diseñador. Razonable pero pendiente de
  confirmación. Está claramente listada en la sección 3.
- `[Diferido]` — fuera de alcance de v1; se anota para no perderlo.
- `[Pendiente]` — la decisión existe pero se cierra antes del 02-plan o
  antes de implementar el bloque correspondiente.

Este documento describe **qué construir y por qué**, no el código. La
implementación seguirá las convenciones del repo (hexagonal, CQRS con
MediatR, EF Core, FluentValidation, Serilog, records, sealed por
defecto, nullable reference types).

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

- **Módulo:** `Compras` (mismo módulo que Requisiciones).
- **Submódulo:** `Ordenes de Compra` (este documento).
- **Esquema PostgreSQL:** `compras` (compartido con Requisiciones).
- **Bounded context:** `Compras`. La requisición y la orden de compra
  conviven en el mismo BC porque comparten lenguaje, reglas de
  autorización, proveedores y trazabilidad de cubrimiento.
- **Proyecto .NET:** `backend/src/Compras/` (mismo proyecto). El
  submódulo agrega entidades, comandos y endpoints bajo namespaces
  `Millet.Compras.Domain.OC`, `Millet.Compras.Application.OC` y
  `Millet.Api.Endpoints.Compras.OC` para mantenerlo separado pero
  cohesivo.

### 1.2 Alcance funcional v1

**Dentro:**

1. CRUD de OC (cabecera + líneas + información logística + información
   de importación opcional).
2. Workflow de autorización en **dos niveles**, a nivel de cabecera
   (Jefe de Compras → Dirección).
3. **Dos flujos de creación:**
   - **1:1 desde requisición autorizada** (bandeja de RQ → "convertir
     en OC").
   - **N:1 consolidación desde el módulo de Compras** (selector de RQs
     disponibles).
4. **Restricción de selector por sucursal única** (cerrado en §10.5 del
   mapa funcional).
5. **Trazabilidad línea-a-línea** con la requisición de origen
   (consolidación preserva separación de renglones, no suma cantidades).
6. **Compromiso exclusivo de RQ**: una RQ autorizada solo puede estar
   en una OC activa a la vez. Cancelar OC en Borrador libera RQs
   automáticamente.
7. **Sub-estados independientes** (Recepción / Facturación / Pago) que
   avanzan en paralelo y resuelven el cierre automático.
8. **Información logística estructurada** desde MVP (transportista,
   guía, contenedor, ruta, semana, pedimento, país de origen) — no
   texto libre embebido como en SAP.
9. **Adjuntos genéricos** (cotización obligatoria, ficha técnica para
   importaciones, correos de autorización, pedimentos).
10. Generación automática de **PDF al autorizar**, agrupado por
    artículo para el proveedor (oculta desglose interno por RQ).
11. **Cancelación con liberación de RQs** (parcial si hubo recepción).
12. **Duplicación de OC cancelada/rechazada** vía
    `DuplicarOrdenCompraCommand` para mitigar el costo operativo de
    "cancelar + recrear" sin reapertura.
13. **Bandejas y reportes** (mis borradores, pendientes autorización,
    partidas abiertas, últimas 100 compras, árbol de documentos).
14. **Listener de eventos de Recepción y Factura** para mantener
    sub-estados sincronizados.
15. Notificaciones por evento (vía módulo Notificaciones).
16. Auditoría (vía framework del ERP, ADR 0008).

**Fuera (cerrado en mapa funcional §10):**

- **Cliente final destinatario** en OC — `[Diferido a Fase 2]`. El
  campo se reserva en el modelo de dominio como hueco no operado en
  v1; no aparece en pantallas ni en payloads.
- **Cotizaciones formales** como entidad separada (cotización vive solo
  como adjunto).
- **Portal para proveedores** (proveedores no acceden al sistema).
- **Ejecución de pagos** (vive en Tesorería; este submódulo solo
  refleja el estado de pago como `SubEstadoPago`).
- **Multi-moneda activa** — `[Asunción C1]`: el VO `Money` y el campo
  `Moneda` soportan otras monedas pero v1 se calibra para MXN. Las OCs
  de importación pueden capturar moneda extranjera con tipo de cambio
  manual; el reporteo agregado se hace en MXN convertido al tipo de
  cambio del día de autorización.
- **CRUD de catálogos** (proveedores, artículos, condiciones de pago,
  incoterms, transportistas, regímenes fiscales, etc.) — `[Diferido]`
  post-MVP, como bloque separado posterior al go-live. En MVP los
  catálogos se operan con **seeds versionados** + endpoints `GET`
  read-only.
- **Importación inicial de catálogos y OCs históricas desde SAP** —
  `[Diferido]` post-MVP. No forma parte del scope del submódulo OC v1.
  El modelo soporta migración aditiva si se reabre.
- **Servicios externos de validación CFDI** y **modo offline** —
  `[Diferido]`; no se mencionan en docs derivados.
- **Multi-empresa SAP** — ver ADR 0011. La OC hereda el patrón de
  Requisiciones: `EmpresaId` en agregado.
- **Bulk operations** (autorización masiva, edición masiva) — v1.1.

### 1.3 Volúmenes esperados

Del mapa funcional §11:

- 15 a 45 OCs nuevas por día (~360–900/mes).
- 15 a 50 entradas de mercancía por día.
- 15 a 50 facturas por día.
- Picos viernes, lunes y fin de mes.
- 3 usuarios concurrentes típicos en Compras.

**Implicación de diseño:** sin requerimientos especiales de escala; un
solo nodo .NET sirve. Los índices del §10 deben cubrir las bandejas y
los selectores; las consultas de partidas abiertas se evaluarán contra
volumen real y se promueven a vista materializada si degradan
(post-v1).

---

## 2. Decisiones de diseño y ADRs aplicados

Hereda los ADRs del módulo Compras. La tabla resume los aplicados; el
detalle vive en cada ADR.

| Tema | Decisión | Referencia |
|---|---|---|
| Identidad y autorización | Entra ID + RBAC granular por permisos | ADR 0003, ADR 0007 |
| Concurrencia | Optimista por `Version` `IsConcurrencyToken` (Capa 1) + Soft lock vía SignalR (Capa 2) | ADR 0012 |
| Auditoría | Framework centralizado, no columnas duplicadas en cada tabla | ADR 0008 |
| Migraciones | EF Core, esquema por módulo | ADR 0005 |
| Validación | FluentValidation por comando | ADR 0018 |
| Eventos de integración | Outbox + Service Bus | ADR 0009 |
| Errores HTTP | Problem Details (RFC 7807) | ADR 0010 |
| Versionado API | URL versioned (`/api/v1/...`) | ADR 0021 |
| Idempotencia HTTP | Header `Idempotency-Key` en POST que crean recursos | ADR 0020 |
| Notificaciones email | Vía módulo Notificaciones | ADR 0026 |
| Multi-empresa | `EmpresaId` en agregados con datos por sociedad | ADR 0011 |
| Tiempo / zonas | UTC en BD, conversión en presentación | ADR 0013 |
| Multimoneda | Value object `Money`. v1 fija MXN. | ADR 0014 |
| Adjuntos | Blob storage (no `image` en BD) | ADR 0024 |
| Stubs cross-module y deuda de plataforma | Sección "Dependencias de plataforma pendientes" + `PLATFORM-TODO` en código | ADR 0031 |

> Los ADRs son la fuente de verdad. Si este documento contradice un
> ADR, gana el ADR.

---

## 3. Asunciones que deben confirmarse

Estas asunciones son las del diseño v1. Si el cliente las descarta,
algunas secciones cambian. Cierran en sesión de validación del 01.

| # | Asunción | Default propuesto | Si el cliente dice "no" |
|---|---|---|---|
| C1 | **Multimoneda en MVP** | `Money` soporta otras monedas; v1 calibra MXN. Importaciones capturan moneda extranjera con tipo de cambio **manual** (campo libre, no servicio externo). Reporteo agregado en MXN convertido al T/C del día de autorización. | Si exigen servicio de T/C automático (Banxico, etc.), se agrega puerto `ITipoCambioPort` con stub `[Asunción]` apuntando a valor manual hasta integrar. |
| C2 | **Formato del folio interno** | `OC-{prefijoSucursal}{año}-{secuencial:6}` (ej. `OC-MID2026-000001`). Generación atómica vía secuencia PostgreSQL por (sucursal, año). | Si el cliente prefiere folio por departamento o por comprador, se ajusta el VO y la secuencia. |
| C3 | **Cálculo de IVA y retenciones** | Por **régimen fiscal del proveedor + régimen del artículo**. Catálogo `Regimenes` con tasas vigentes con vigencia desde/hasta. Cálculo en el agregado de OC; no se delega al motor contable v1. | Si el cliente exige delegar a Contabilidad, se agrega puerto `ICalcularImpuestosPort` con stub local. |
| C4 | **Modificación post-autorización: cancelar + recrear (cerrado)** | **Decisión cerrada (2026-05-11):** una OC autorizada **no se modifica**. Si el comprador necesita cambios, **cancela** la OC con motivo y crea una nueva — apoyado por el comando `DuplicarOrdenCompraCommand` que pre-llena cabecera + líneas desde la OC cancelada/rechazada, con campo `oc_origen_id` para trazabilidad bidireccional. **No hay** estado `EnReautorizacion`, **no hay** entidad `VersionOC`, **no hay** snapshot. | — |
| C5 | **Cliente final destinatario diferido** | El campo **no existe en el modelo de dominio v1**. Cuando se desbloquee Fase 2, se agrega como migración aditiva (nullable, snapshot de A+W con `ClienteExternoId`). | Si el cliente lo pide para MVP, se reabre la decisión §10.4 del mapa funcional. |
| C6 | **Catálogo de tipos de documento adjunto** | Tabla `compras.tipos_documento_oc` con: `cotizacion`, `ficha_tecnica`, `correo_autorizacion`, `pedimento`, `factura_proveedor_extranjero`, `packing_list`, `otro`. Algunos marcados `obligatorio_si_importacion`. | Si el cliente quiere más tipos o renombra, se actualiza el seed. |
| C7 | **Sub-estados como columnas materializadas** | `sub_estado_recepcion`, `sub_estado_facturacion`, `sub_estado_pago` se persisten como columnas en `compras.ordenes_compra`, actualizadas por los handlers de los eventos de Recepción/Factura/Pago. **Alternativa rechazada**: derivar siempre por agregación — caro para bandejas con alto tráfico. | Si el equipo prefiere derivar por agregación, se reemplaza por vista materializada. |
| C8 | **Estructura de líneas en consolidación** | Cada línea preserva trazabilidad 1:1 con su renglón de RQ de origen (no se suman cantidades del mismo artículo). El PDF al proveedor agrupa por artículo en una transformación de presentación, no en el modelo de datos. | Si el cliente exige suma interna (rechazado en §10.4 del mapa funcional), se modifica `LineaOrdenCompra` para soportar múltiples FKs a `LineaRequisicion`. |
| C9 | **Modificación de RQ comprometida** | Mientras una RQ esté comprometida en una OC (Borrador, EnAutorizacion, Autorizada), **no se puede modificar ni eliminar** desde el submódulo de Requisiciones. La RQ queda en modo lectura. Esto requiere ampliar el agregado `Requisicion` con bandera `comprometida_en_oc_id`. | Si se prefiere permitir edición con propagación, se modela como dependencia bidireccional con notificación al comprador. |
| C10 | **Validez del proveedor al autorizar** | Se valida que el proveedor esté `Activo` **en cada transición de estado** (al enviar a autorización y al autorizar). Si el proveedor se bloquea entre captura y autorización, se rechaza con motivo. | Si se prefiere validar solo al crear, se reduce el checkpoint a una validación inicial. |
| C11 | **Adjuntos: cotización obligatoria con excepción** | Antes de enviar a autorización, el sistema requiere al menos un adjunto con `TipoDocumento = cotizacion`. Excepción: bandera `cotizacion_excepcionada` en cabecera + adjunto tipo `correo_autorizacion`. La bandera no se puede activar sin adjuntar el correo. | Si el cliente prefiere validar fuera del agregado (con override manual), se mueve a regla de aplicación con auditoría. |
| C12 | ~~Versión en bitácora post-autorización~~ | **N/A** — descartado al cerrar C4 como "cancelar + recrear". La trazabilidad entre OC origen y OC nueva se preserva vía campo `oc_origen_id` en la cabecera. | — |

---

## 3.bis Conceptos derivados de las decisiones

### 3.bis.1 Compromiso exclusivo de RQ y selector

Una RQ autorizada puede estar en **uno y solo uno** de estos lugares
al mismo tiempo:

- **Pool de disponibles** (sin OC asociada, o con OC en estado terminal
  Cancelada/Rechazada).
- **Comprometida en OC activa** (Borrador, EnAutorizacion, Autorizada).

Modelo: el agregado `Requisicion` se amplía con un campo nullable
`comprometida_en_oc_id : OrdenCompraId?`. Se setea al crear la OC
(flujo §4.1 / §4.2) y se limpia al cancelar la OC en Borrador o al
quitar líneas de la OC en edición.

Selectores (§4.1 bandeja de RQ → "convertir en OC"; §4.2 selector
multiselección en módulo de Compras) filtran por:

```sql
WHERE r.estado = 'EnSurtido'
  AND r.comprometida_en_oc_id IS NULL
  AND r.sucursal_id = :sucursal_oc   -- restricción §10.5
```

> **Cambio 2026-05-13 (ADR-0033)**: el filtro estaba documentado
> originalmente como `estado = 'Autorizada'`, alineado con la suposición
> A3 del diseño RQ ("la OC se genera automática al autorizar"). La
> revisión de A3 introduce el setting `auto_generar_oc_al_autorizar`
> (default false). Cuando es false, el handler de Autorizar pone la RQ
> en `EnSurtido` con saldo de compra y deja al comprador convertirla
> manualmente; ese es el estado en el que las RQs son "disponibles" para
> 1:1 o N:1. Cuando es true, el handler comprometía implícitamente la
> RQ vía el puerto OC — esas RQs salen del pool automáticamente. El
> filtro unificado `EnSurtido AND comprometida_en_oc_id IS NULL`
> funciona en ambos modos.

> **Implicación de migración**: cuando este submódulo se mergee, el
> agregado `Requisicion` recibe la columna `comprometida_en_oc_id`.
> Migración aditiva, nullable, sin impacto en datos existentes. Las RQs
> ya autorizadas en producción aparecerán como "disponibles" en el
> selector (correcto: si no tienen OC, están disponibles).

> **Cantidad heredada en la línea de OC**: los handlers
> (`CrearOrdenCompraDesdeRequisicionHandler` 1:1,
> `AgregarLineaDesdeRequisicionHandler` N:1) heredan la línea de la RQ
> con `cantidad = linea_rq.cantidad_de_compra` — solo el saldo
> pendiente de compra, no la cantidad original. La parte de almacén ya
> fue cubierta con reserva + movimiento de salida al autorizar.

### 3.bis.2 Sub-estados independientes

La OC autorizada tiene tres dimensiones de progreso que avanzan
**independientemente**:

| Dimensión | Valores | Fuente de actualización |
|---|---|---|
| `SubEstadoRecepcion` | `SinRecepcion`, `Parcial`, `Completa` | Listener de `OcRecepcionRegistradaEvent` (Almacén) |
| `SubEstadoFacturacion` | `SinFactura`, `Parcial`, `Completa` | Listener de `FacturaProveedorRegistradaEvent` (CxP) |
| `SubEstadoPago` | `SinPago`, `Parcial`, `Pagada` | Listener de `PagoFacturaProveedorEvent` (Tesorería) |

**Cálculo:**

- `SubEstadoRecepcion`:
  - `SinRecepcion` si `SUM(linea.cantidad_recibida) = 0`.
  - `Parcial` si `0 < SUM(linea.cantidad_recibida) < SUM(linea.cantidad)`.
  - `Completa` si `SUM(linea.cantidad_recibida) = SUM(linea.cantidad)`.
- `SubEstadoFacturacion`: equivalente, contra `cantidad_facturada`.
- `SubEstadoPago`: equivalente, contra `monto_pagado` vs `total_facturado`.

**Transición a `Cerrada`:** se ejecuta automáticamente cuando las tres
dimensiones son `Completa` / `Completa` / `Pagada` y la línea no tiene
devoluciones pendientes (§3.bis.5).

> **Por qué columnas materializadas (C7):** las bandejas de partidas
> abiertas filtran por sub-estado todo el tiempo (vista crítica del
> negocio según §8.2 del mapa funcional). Recalcular sobre `lineas`
> en cada query agrega cost por agregación; con columnas materializadas
> el índice combinado `(empresa_id, sub_estado_recepcion,
> sub_estado_facturacion, sub_estado_pago)` cubre la bandeja.

### 3.bis.3 Información logística estructurada

Hoy en SAP los compradores codifican logística como texto libre en
comentarios:

```
RUTA:6515 CONTENEDOR:SEGU6397527 CLIENTE:BCI CRAWFORD SEMANA:05/DICIEMBRE/2025
```

El ERP nuevo modela esto como **dos value objects** en la cabecera de
la OC:

```text
InformacionLogistica (VO inmutable, always present)
├── DireccionEntrega         : string?
├── TransportistaId          : TransportistaId?  ← catálogo
├── TransportistaTexto       : string?           ← libre si no en catálogo
├── NumeroGuia               : string?
└── InstruccionesEnvio       : string?

InformacionImportacion (VO inmutable, nullable — solo si EsImportacion)
├── IncotermId               : IncotermId
├── PaisOrigen               : string (ISO 3166-1 alpha-2)
├── NumeroContenedor         : string
├── CodigoRuta               : string
├── SemanaEmbarque           : string             ← formato "DD/MES/YYYY" o ISO week
└── NumeroPedimento          : string?            ← se captura al recibir
```

Todos los campos de ambos VOs son **indexables y filtrables** en
bandejas y reportes (§10.3).

> **Cliente final destinatario diferido a Fase 2 (C5):** no aparece en
> `InformacionLogistica` v1. Cuando se desbloquee, se agrega como campo
> nullable con snapshot `ClienteExternoId + ClienteSnapshot
> (nombre, rfc, codigo)`.

### 3.bis.4 Trazabilidad de líneas y agrupación para PDF

**En el modelo de datos** las líneas se preservan tal como vienen de
las requisiciones: si la consolidación trae el artículo `ART-001` desde
RQ-001 (cantidad 5) y desde RQ-002 (cantidad 3), la OC tendrá **dos
líneas separadas** del mismo artículo:

| Posición | ArtículoId | Cantidad | RequisicionId | LineaRequisicionId |
|---|---|---|---|---|
| 1 | ART-001 | 5 | RQ-001 | linea-de-RQ-001 |
| 2 | ART-001 | 3 | RQ-002 | linea-de-RQ-002 |

**En el PDF al proveedor** (transformación de presentación, no del
modelo) se agrupa por artículo:

| Línea | Artículo | Cantidad |
|---|---|---|
| 1 | ART-001 | 8 |

El servicio `GenerarPdfOcService` ejecuta el `GROUP BY articulo_id`
con `SUM(cantidad)`, y oculta los campos `requisicion_id`,
`linea_requisicion_id` y `departamento_solicitante_id` (información
interna). Esto preserva trazabilidad cuando se reciba parcial: la
recepción atribuye al renglón correcto, no requiere repartir manualmente
las cantidades por requisición de origen.

### 3.bis.5 Devoluciones a proveedores (referencia)

Las devoluciones son un sub-flujo del **submódulo de Recepción** (ver
mapa funcional §9.5), no de OC. Sin embargo, afectan el ciclo de vida
de la OC: una OC con devolución pendiente **no avanza a Cerrada**
mientras el ajuste fiscal y de inventario no esté completo.

**Cómo impacta el modelo de OC en v1:**

- El listener `DevolucionProveedorRegistradaListener` (TODO post-v1
  de Recepción) decrementa `cantidad_recibida` en la línea de OC
  correspondiente.
- Si la devolución implica nota de crédito, otro listener decrementa
  `cantidad_facturada` y, si aplica, `monto_pagado`.
- La OC vuelve a `SubEstadoRecepcion = Parcial` (o `SinRecepcion`) y
  no transiciona a `Cerrada`.

En v1 del submódulo OC, los listeners están preparados como contratos
en `Domain/Events` y `Application/Eventos` pero la implementación real
de devoluciones es del submódulo de Recepción.

### 3.bis.6 Concurrencia (alineado con ADR 0012)

Aplica el mismo modelo híbrido de Requisiciones:

- **Capa 1 (siempre):** `OrdenCompra` hereda `BaseEntity` con
  `Version`/`IsConcurrencyToken`. Conflictos generan 409 con Problem
  Details (ADR 0010); el frontend usa `<ConflictResolutionDialog />`.
- **Capa 2 (soft lock vía SignalR):** `OrdenCompra` se declara en la
  lista de entidades con awareness colaborativo del módulo. El hook
  `useCollaboration("orden-compra", id)` muestra "Pedro está editando"
  cuando dos compradores tienen el mismo documento abierto.
- **No aplica Capa 3 (hard lock):** la whitelist del ADR no incluye
  OC. Conflictos no son catastróficos porque la Capa 1 garantiza no
  perder datos.

> **PLATFORM-TODO(`<CollaborationHub>`)**: declarar `OrdenCompra` en
> la lista de entidades con soft lock cuando el hub exista. Mientras
> tanto, el stub `NoOp` heredado del módulo aplica.

---

## 4. Modelo de dominio

> **Workstream almacén-por-línea PR2 (OC sin almacén).** La OC **ya no captura
> almacén destino** en ningún nivel: se eliminaron `OrdenCompra.AlmacenDestinoDefaultId`
> (cabecera, era el 4º sentinel del borrador-mínimo → ahora 3) y
> `LineaOrdenCompra.AlmacenDestinoId` (línea) — DROP de ambas columnas +
> índice `ix_oc_lineas_almacen`. Motivo: el comprador no sabe a qué almacén
> entra el material; solo el almacenista lo decide al recibir (misma lógica que
> la RQ). El reorden (ADR-0047), que deduplicaba el "pedido vivo" por
> `(artículo, almacén de la línea de OC)`, ahora lee el almacén desde la **RQ de
> origen** vía join por `RequisicionId` en `ComprasPedidoVivoReadAdapter` —
> valor idéntico (la copia RQ→OC ya los igualaba), dedup intacto. Cero
> acoplamiento cross-módulo (ningún integration event ni puerto de lectura
> exponía el campo). Los diagramas de abajo conservan las líneas
> `AlmacenDestino*` como historia de diseño.

### 4.1 Agregado raíz: `OrdenCompra`

```text
OrdenCompra (agregado raíz)
├── OrdenCompraId           : OrdenCompraId (Guid v7, identidad)
├── EmpresaId               : EmpresaId          ← ADR 0011
├── Folio                   : Folio (VO)
├── ReferenciaProveedor     : string?            ← folio externo del proveedor, indexado
├── ProveedorId             : ProveedorId
├── ContactoProveedor       : ContactoProveedor (VO, snapshot)
├── SucursalDestinoId       : SucursalId
├── AlmacenDestinoDefaultId : AlmacenId
├── Moneda                  : Moneda             ← default MXN
├── TipoCambio              : decimal?           ← required si Moneda ≠ MXN
├── CondicionesPagoId       : CondicionesPagoId
├── UsoPrincipalId          : UsoPrincipalId     ← clasificación contable
├── EncargadoComprasId      : UsuarioId          ← reasignable
├── CompradorTitularId      : UsuarioId          ← creador, inmutable
├── Observaciones           : string?
├── SinRequisicionPrevia    : bool
├── EsImportacion           : bool
├── CotizacionExcepcionada  : bool               ← C11
├── FechaDocumento          : DateTime
├── FechaContabilizacion    : DateTime?          ← set al autorizar
├── FechaEntregaEsperada    : DateTime?
├── Estado                  : EstadoOrdenCompra (enum)
├── SubEstadoRecepcion      : SubEstadoRecepcion (enum)
├── SubEstadoFacturacion    : SubEstadoFacturacion (enum)
├── SubEstadoPago           : SubEstadoPago (enum)
├── InformacionLogistica    : InformacionLogistica (VO)
├── InformacionImportacion  : InformacionImportacion? (VO, si EsImportacion)
├── DescuentoGlobal         : DescuentoGlobal (VO)  ← porcentaje o monto
├── GastosAdicionales       : Money
├── Redondeo                : Money
├── Totales                 : TotalesOC (VO computed)
├── MotivoSinRequisicion    : string?            ← si SinRequisicionPrevia
├── MotivoCancelacion       : string?            ← si Estado == Cancelada
├── MotivoRechazoId         : MotivoRechazoId?   ← reusa catálogo de RQ
├── MotivoRechazoTexto      : string?
├── Lineas                  : List<LineaOrdenCompra>
├── Autorizaciones          : List<AutorizacionOC>
├── Adjuntos                : List<AdjuntoOC>
├── OcOrigenId              : OrdenCompraId?     ← C4: trazabilidad si fue duplicada
└── Version                 : uint               ← optimistic concurrency
```

**Invariantes del agregado:**

- `Lineas.Count >= 1` cuando `Estado != Borrador`.
- `Folio` único por `(EmpresaId, SucursalDestinoId, año)`.
- `TipoCambio` es requerido cuando `Moneda != MXN` y `> 0`.
- `InformacionImportacion` requerido cuando `EsImportacion == true`.
- Si `SinRequisicionPrevia == true`: `MotivoSinRequisicion` no nulo y
  al menos un adjunto tipo `correo_autorizacion`.
- Si `EsImportacion == true`: antes de autorizar, al menos un adjunto
  tipo `ficha_tecnica`.
- Antes de enviar a autorización: al menos un adjunto tipo
  `cotizacion`, **salvo** que `CotizacionExcepcionada == true` y exista
  adjunto tipo `correo_autorizacion`.
- Una OC con `MotivoCancelacion != null` no puede recibir comandos de
  modificación.
- Las transiciones de `Estado` solo se permiten vía métodos del
  agregado (`EnviarAAutorizacion()`, `Autorizar(nivel)`,
  `Rechazar(motivo)`, `Cancelar(motivo)`, etc.) y respetan la state
  machine (§5).
- Una `AutorizacionOC` con `Nivel.Nivel2` solo se acepta si ya existe
  una `Nivel1` con `Resultado = Autorizado`.
- `CompradorTitularId` se setea en `Crear()` y nunca cambia.
- `Totales` es computed; no aceptar overrides externos.

**Comportamientos clave (métodos públicos):**

- `Crear(...)` → constructor estático. Estado inicial = `Borrador`.
  Existen tres variantes:
  - `CrearDesdeRequisicion(rqId, ...)` — flujo §4.1.
  - `CrearVacia(...)` — flujo §4.2, para consolidación posterior.
  - `DuplicarDesde(ocOrigenId, ...)` — flujo C4: pre-llena cabecera +
    líneas desde una OC cancelada/rechazada. **No copia**: adjuntos
    (cotización debe ser nueva), autorizaciones, eventos previos, ni
    referencias a RQs (que fueron liberadas al cancelar la OC origen
    o deben re-seleccionarse). Setea `OcOrigenId`.
- `AgregarLineaDesdeRequisicion(rqId, lineaRqId, articuloId, cantidad,
  precio, ...)` → agrega línea con FK a la RQ. La RQ se marca como
  comprometida (efecto cruzado vía evento `RqComprometidaEnOcEvent`).
- `AgregarLineaManual(articuloId, cantidad, precio, ...)` → solo
  cuando `SinRequisicionPrevia == true`.
- `ActualizarLinea(lineaId, ...)` → solo en `Borrador` o `Rechazada`.
- `EliminarLinea(lineaId)` → solo en `Borrador` o `Rechazada`. Si la
  línea venía de una RQ, emite `LineaRqLiberadaEvent` para que la RQ
  vuelva al pool.
- `ActualizarCabecera(...)` → solo en `Borrador` o `Rechazada`.
- `ActualizarInformacionLogistica(...)` → editable en `Borrador`,
  `Rechazada` o `Autorizada` (campos como `NumeroGuia` se actualizan
  durante el ciclo de recepción sin re-autorización).
- `AdjuntarDocumento(adjunto)` → permitido en cualquier estado no
  terminal.
- `RemoverAdjunto(adjuntoId)` → solo en `Borrador`.
- `EnviarAAutorizacion()` → valida invariantes pre-autorización
  (cotización adjunta o excepcionada, ficha técnica si importación,
  motivo si sin RQ previa, etc.); transiciona a
  `EnAutorizacionJefeCompras`; emite
  `OrdenCompraEnviadaAAutorizacionEvent`.
- `RegistrarAutorizacion(usuarioId, nivel, resultado, motivo?,
  motivoTexto?)` → valida permiso, secuencia (N1 antes de N2) y
  proveedor activo (C10). Si `resultado = Rechazado`, transiciona a
  `Rechazada` con motivo (reusa `MotivoRechazo` de RQ). Si
  `resultado = Autorizado` en N1, transiciona a
  `EnAutorizacionDireccion`. Si `Autorizado` en N2, transiciona a
  `Autorizada`, setea `FechaContabilizacion = now()`, emite
  `OrdenCompraAutorizadaEvent` (consumido por servicio de PDF y por
  Recepción).
- `RegistrarRecepcionLinea(lineaId, cantidadRecibida)` → llamado por
  listener de `OcRecepcionRegistradaEvent` (Almacén). Incrementa
  `cantidad_recibida` en la línea. Recalcula `SubEstadoRecepcion`. Si
  las 3 dimensiones cierran, transiciona a `Cerrada` (emite
  `OcCerradaEvent`).
- `RegistrarFacturaLinea(lineaId, cantidadFacturada)` → equivalente
  para factura. Listener de CxP.
- `RegistrarPagoFactura(facturaId, monto)` → equivalente para pago.
  Listener de Tesorería.
- `Cancelar(motivoId, motivoTexto?, usuarioId)` → permitido en
  `Borrador`, `EnAutorizacionJefeCompras`, `EnAutorizacionDireccion`,
  `Autorizada` (sin recepciones), o `Autorizada` con recepciones
  parciales (con doble autorización registrada). Emite
  `OrdenCompraCanceladaEvent` que libera RQs (parcial si hay
  recepción).
- *(C4 descartó reapertura — `ReabrirParaEdicion` y
  `RegistrarVersionPostReautorizacion` no existen en v1. Para
  modificar una OC autorizada: cancelar + duplicar.)*

### 4.2 Entidad: `LineaOrdenCompra`

```text
LineaOrdenCompra
├── LineaId                 : LineaOrdenCompraId
├── OrdenCompraId           : OrdenCompraId   ← FK al agregado
├── Posicion                : int
├── ArticuloId              : ArticuloId
├── DescripcionExtendida    : string?
├── Cantidad                : decimal
├── UnidadMedida            : string          ← snapshot del catálogo
├── PrecioUnitario          : Money
├── Descuento               : DescuentoLinea (VO)  ← porcentaje o monto
├── IndicadorImpuestos      : IndicadorImpuestos (VO)  ← hereda de proveedor/artículo
├── IvaImporte              : Money           ← calculado
├── RetencionIsr            : Money?          ← calculada si aplica
├── AlmacenDestinoId        : AlmacenId       ← default cabecera
├── DepartamentoSolicitanteId : DepartamentoId  ← consolidación cross-depto
├── RequisicionId           : RequisicionId?  ← null si SinRequisicionPrevia
├── LineaRequisicionId      : LineaRequisicionId?  ← FK trazabilidad
├── FechaEntregaLinea       : DateTime?
├── CantidadRecibida        : decimal         ← incremental, máx Cantidad
├── CantidadFacturada       : decimal         ← incremental, máx Cantidad
├── TextoAdicional          : string?
└── SubtotalLinea           : Money           ← computed
```

**Invariantes:**

- `Cantidad > 0`.
- `PrecioUnitario.Amount >= 0`.
- `Posicion` única dentro de la OC.
- `CantidadRecibida <= Cantidad`.
- `CantidadFacturada <= Cantidad`.
- Si `RequisicionId != null`, entonces `LineaRequisicionId != null` y
  la RQ debe estar en estado `Autorizada` al momento de captura.
- `DepartamentoSolicitanteId` debe ser válido (catálogo activo).
- Una vez que `CantidadRecibida > 0` o `CantidadFacturada > 0`, los
  campos estructurales (`ArticuloId`, `Cantidad`, `PrecioUnitario`,
  `UnidadMedida`, `AlmacenDestinoId`) **no se pueden modificar**.
  `TextoAdicional` y `FechaEntregaLinea` sí se pueden modificar (sin
  re-autorización).
- `SubtotalLinea = (Cantidad * PrecioUnitario) - Descuento` (computed
  por agregado; no aceptar overrides).

> **Por qué `RequisicionId` denormalizado además de
> `LineaRequisicionId`:** las bandejas y reportes filtran por
> `RequisicionId` frecuentemente (árbol de documentos §8.4). Tenerlo
> denormalizado en la línea evita join adicional a `lineas_requisicion`.

### 4.3 Value object: `Folio`

```text
Folio
└── Valor : string    ← formato configurable por sucursal
```

Reglas (asunción C2):

- Formato default: `OC-{prefijoSucursal}{año}-{secuencial:6}`
  (ej. `OC-MID2026-000001` para sucursal "Mérida").
- Unicidad por `(EmpresaId, SucursalDestinoId, año)`.
- Generación atómica vía secuencia PostgreSQL por sucursal+año
  (similar patrón al de Requisiciones: `compras.folio_secuencias_oc`).

### 4.4 Value object: `ReferenciaProveedor`

```text
ReferenciaProveedor
└── Valor : string?    ← folio externo del proveedor
```

- Captura libre, opcional.
- Indexado para búsqueda por igualdad y prefijo (§10.3).
- Trim + normalización a mayúsculas al persistir.

### 4.5 Value object: `ContactoProveedor`

```text
ContactoProveedor (snapshot al momento de captura)
├── Nombre        : string
├── Email         : string?
└── Telefono      : string?
```

- Snapshot del contacto principal del proveedor al momento de capturar
  la OC. Si el contacto del proveedor cambia después, la OC mantiene
  el snapshot histórico.

### 4.6 Value object: `InformacionLogistica`

```text
InformacionLogistica
├── DireccionEntrega       : string?
├── TransportistaId        : TransportistaId?
├── TransportistaTexto     : string?    ← libre si no en catálogo
├── NumeroGuia             : string?
└── InstruccionesEnvio     : string?
```

- Cero o uno de `TransportistaId` y `TransportistaTexto` (no ambos).
- `NumeroGuia` editable post-autorización (sin re-autorización).

### 4.7 Value object: `InformacionImportacion`

```text
InformacionImportacion (nullable, solo si EsImportacion == true)
├── IncotermId             : IncotermId
├── PaisOrigen             : string      ← ISO 3166-1 alpha-2
├── NumeroContenedor       : string
├── CodigoRuta             : string
├── SemanaEmbarque         : string
└── NumeroPedimento        : string?     ← se captura al recibir
```

- `IncotermId`, `PaisOrigen`, `NumeroContenedor`, `CodigoRuta`,
  `SemanaEmbarque` requeridos al autorizar.
- `NumeroPedimento` opcional al autorizar, editable post-autorización
  (se captura cuando llega el material).

### 4.8 Value object: `DescuentoGlobal` y `DescuentoLinea`

```text
DescuentoGlobal
├── Tipo       : DescuentoTipo (enum: Porcentaje, Monto)
└── Valor      : decimal      ← 0..100 si Porcentaje, ≥0 si Monto

DescuentoLinea
├── Tipo       : DescuentoTipo
└── Valor      : decimal
```

- Aplican antes de calcular impuestos.
- `Porcentaje` debe estar en `[0, 100]`.
- `Monto` debe ser `>= 0`.

### 4.9 Value object: `TotalesOC` (computed)

```text
TotalesOC (immutable, recalculado en cada cambio de líneas/cabecera)
├── SubtotalAntesDescuento  : Money
├── DescuentoGlobal         : Money
├── GastosAdicionales       : Money
├── BaseGravable            : Money
├── IvaTotal                : Money
├── RetencionIsrTotal       : Money
├── Redondeo                : Money
└── TotalAPagar             : Money
```

Fórmula:

```
SubtotalAntesDescuento = SUM(linea.SubtotalLinea)
BaseGravable           = SubtotalAntesDescuento - DescuentoGlobal + GastosAdicionales
IvaTotal               = SUM(linea.IvaImporte)
RetencionIsrTotal      = SUM(linea.RetencionIsr ?? 0)
TotalAPagar            = BaseGravable + IvaTotal - RetencionIsrTotal + Redondeo
```

### 4.10 Entidad: `AutorizacionOC`

```text
AutorizacionOC
├── AutorizacionOcId       : AutorizacionOcId
├── OrdenCompraId          : OrdenCompraId
├── Nivel                  : NivelAutorizacion (enum: Nivel1, Nivel2)
├── Resultado              : ResultadoAutorizacion (enum: Autorizado, Rechazado)
├── UsuarioId              : UsuarioId
├── FechaHora              : DateTime (UTC)
├── MotivoRechazoId        : MotivoRechazoId?    ← reusa catálogo de RQ
├── MotivoRechazoTexto     : string?
└── Notas                  : string?
```

**Invariantes:**

- Una `(OrdenCompra, Nivel, Resultado = Autorizado)` puede tener una
  sola autorización exitosa. Rechazos múltiples permitidos (cada vuelta
  registra uno).
- `Nivel2 Autorizado` requiere que exista `Nivel1 Autorizado` previo.
- `Resultado = Rechazado` requiere `MotivoRechazoId`. Si el motivo
  permite texto libre, `MotivoRechazoTexto` es requerido.

### 4.11 Entidad: `AdjuntoOC`

```text
AdjuntoOC
├── AdjuntoId              : AdjuntoOcId
├── OrdenCompraId          : OrdenCompraId
├── TipoDocumento          : TipoDocumentoOc (catálogo C6)
├── NombreArchivo          : string
├── BlobUrl                : string         ← ref a blob storage (ADR 0024)
├── ContentType            : string         ← MIME
├── TamañoBytes            : long
├── FechaCarga             : DateTime (UTC)
└── UsuarioCargaId         : UsuarioId
```

### 4.12 Catálogo de tipos de documento (C6)

```sql
compras.tipos_documento_oc (
    tipo_documento_oc_id    uuid          PK,
    clave                   varchar(40)   UNIQUE NOT NULL,
    descripcion             varchar(200)  NOT NULL,
    obligatorio_si_importacion boolean    NOT NULL DEFAULT false,
    activo                  boolean       NOT NULL DEFAULT true
);
```

Seed inicial:

- `cotizacion` "Cotización del proveedor" (obligatorio en general
  salvo excepción, ver C11).
- `ficha_tecnica` "Ficha técnica del material"
  (`obligatorio_si_importacion = true`).
- `correo_autorizacion` "Correo o documento de autorización"
  (requerido si `SinRequisicionPrevia` o si `CotizacionExcepcionada`).
- `pedimento` "Pedimento de importación" (al recibir).
- `factura_proveedor_extranjero` "Factura del proveedor extranjero".
- `packing_list` "Packing list".
- `otro` "Otro" (texto libre en `NombreArchivo`).

---

## 5. Ciclo de vida (state machine)

### 5.1 Estados principales

| Estado | Significado | Editable | Terminal |
|---|---|---|---|
| `Borrador` | Recién creada, editable libremente. | Sí | No |
| `EnAutorizacionJefeCompras` | Esperando primer nivel (Jefe de Compras). | No | No |
| `EnAutorizacionDireccion` | Aprobada por N1; esperando firma de Dirección. | No | No |
| `Autorizada` | Lista para enviar al proveedor y para que arranque la recepción. | Limitado (logística + adjuntos) | No |
| `Cerrada` | Recepción, facturación y pago completos. | No | **Sí** |
| `Cancelada` | No procedió. Con motivo registrado. Libera RQs (parcial si hay recepción). | No | **Sí** |
| `Rechazada` | Autorizador devolvió con motivo. Vuelve a Borrador al editar, o se cancela. | Sí (al volver a Borrador implícito) | No |

### 5.2 Sub-estados (independientes)

| Dimensión | Valores | Actualizado por |
|---|---|---|
| `SubEstadoRecepcion` | `SinRecepcion`, `Parcial`, `Completa` | `OcRecepcionRegistradaEvent` listener |
| `SubEstadoFacturacion` | `SinFactura`, `Parcial`, `Completa` | `FacturaProveedorRegistradaEvent` listener |
| `SubEstadoPago` | `SinPago`, `Parcial`, `Pagada` | `PagoFacturaProveedorEvent` listener |

Estos avanzan solo cuando `Estado = Autorizada`.

### 5.3 Transiciones permitidas

```text
   ┌──────────────┐
   │   Borrador   │── Cancelar(motivo) ──────────────────┐
   └──────┬───────┘                                       │
          │ EnviarAAutorizacion()                         │
          ▼                                               │
   ┌──────────────────────────┐                           │
   │ EnAutorizacionJefeCompras│── Rechazar(motivo) ──┐    │
   └──────┬───────────────────┘                      │    │
          │ Autorizar(N1)                            │    │
          ▼                                          │    │
   ┌──────────────────────┐                          │    │
   │ EnAutorizacionDirec. │── Rechazar(motivo) ──────│    │
   └──────┬───────────────┘                          │    │
          │ Autorizar(N2)                            │    │
          │ [setea FechaContabilizacion,             │    │
          │  emite OrdenCompraAutorizadaEvent,       │    │
          │  marca RQs como comprometidas firmes]    │    │
          ▼                                          │    │
   ┌──────────────┐                                       │
   │  Autorizada  │── Cancelar(motivo, dobleAut si  ──────▶│
   └──┬───────────┘      recepciones parciales)            │
      │                                                    ▼
      │ (eventos de Recepción/Factura/Pago        ┌───────────────┐
      │  avanzan sub-estados)                     │   Cancelada   │
      │                                           └───────┬───────┘
      │ Cuando Sub.Rec=Completa ∧                         │ (terminal)
      │      Sub.Fac=Completa ∧                           │
      │      Sub.Pag=Pagada                               │
      ▼                                                   │
   ┌──────────────┐                                       │
   │   Cerrada    │  (terminal)                           │
   └──────────────┘                                       │
                                                          │
   ┌──────────────────┐                                   │
   │    Rechazada     │── EditarYReenviar() ── (vuelve a Borrador) ──┐
   └──┬───────────────┘                                              │
      │                                                              ▼
      └─────────────────────────────────────────────────────────► Borrador

   (Para modificar una OC Autorizada: Cancelar + Duplicar.
    `DuplicarOrdenCompraCommand(ocOrigenId)` lee la OC origen
    [Cancelada o Rechazada], pre-llena cabecera + líneas en una
    nueva OC en estado Borrador, setea `oc_origen_id` para
    trazabilidad. No copia adjuntos, autorizaciones, ni vínculos
    a RQs.)
```

### 5.4 Notas sobre transiciones

- **`EnAutorizacionJefeCompras → Rechazada`** y
  **`EnAutorizacionDireccion → Rechazada`** ambos registran motivo. La
  OC rechazada puede ser **eliminada por el comprador** (transición a
  `Cancelada` con motivo "rechazo definitivo") o **editada** (vuelve a
  `Borrador`; el rechazo previo queda en bitácora).
- **`Autorizada → Cancelada` con recepciones parciales**: requiere
  doble autorización. Las cantidades ya recibidas permanecen
  asociadas a la OC cancelada (`CantidadRecibida` no se decrementa).
  La RQ correspondiente regresa al pool **solo por la cantidad no
  recibida**.
- **Modificar OC autorizada**: no existe reapertura. La vía es
  `Cancelar` (con doble firma si hay recepciones parciales) seguido
  de `DuplicarOrdenCompra` que crea un Borrador nuevo a partir de la
  OC cancelada. La nueva OC pasa por el flujo normal de autorización.

### 5.5 Equivalencias con los estados del mapa funcional

| Estado del agregado | Etiqueta en UI / mapa funcional |
|---|---|
| `Borrador` | Borrador |
| `EnAutorizacionJefeCompras` | En autorización — Jefe de Compras |
| `EnAutorizacionDireccion` | En autorización — Dirección |
| `Autorizada` | Autorizada |
| `Cerrada` | Cerrada |
| `Cancelada` | Cancelada |
| `Rechazada` | Rechazada |

---

## 6. Casos de uso (comandos y queries)

### 6.1 Comandos

| Comando | Permiso requerido | Estados origen |
|---|---|---|
| `CrearOrdenCompraDesdeRequisicionCommand` | `compras.ordenes.crear` | (nuevo) |
| `CrearOrdenCompraVaciaCommand` | `compras.ordenes.crear` | (nuevo) |
| `DuplicarOrdenCompraCommand` (C4) | `compras.ordenes.crear` | OC origen en `Cancelada` o `Rechazada` |
| `AgregarLineaDesdeRequisicionCommand` | `compras.ordenes.crear` | `Borrador`, `Rechazada` |
| `AgregarLineaManualCommand` | `compras.ordenes.crear` | `Borrador`, `Rechazada` (solo si `SinRequisicionPrevia`) |
| `ActualizarLineaCommand` | `compras.ordenes.crear` | `Borrador`, `Rechazada` |
| `EliminarLineaCommand` | `compras.ordenes.crear` | `Borrador`, `Rechazada` |
| `ActualizarCabeceraCommand` | `compras.ordenes.crear` | `Borrador`, `Rechazada` |
| `ActualizarInformacionLogisticaCommand` | `compras.ordenes.crear` o `compras.ordenes.logistica` | `Borrador`, `Rechazada`, `Autorizada` |
| `AdjuntarDocumentoCommand` | `compras.ordenes.adjuntar` | cualquier no terminal |
| `RemoverAdjuntoCommand` | `compras.ordenes.crear` | `Borrador` |
| `EnviarAAutorizacionCommand` | `compras.ordenes.crear` | `Borrador`, `Rechazada` |
| `AutorizarOrdenCompraCommand` | `compras.ordenes.autorizar.nivel1` o `:nivel2` | correspondiente |
| `RechazarOrdenCompraCommand` | `compras.ordenes.autorizar.nivel1` o `:nivel2` | correspondiente |
| `CancelarOrdenCompraCommand` | `compras.ordenes.cancelar` (y `:cancelar.doble` si aplica) | cualquier no terminal |
| `RegistrarRecepcionLineaCommand` | (interno, listener) | `Autorizada` |
| `RegistrarFacturaLineaCommand` | (interno, listener) | `Autorizada` |
| `RegistrarPagoFacturaCommand` | (interno, listener) | `Autorizada` |

### 6.2 Queries

| Query | Permiso |
|---|---|
| `ObtenerOrdenCompraPorIdQuery` | `compras.ordenes.leer` |
| `ListarOrdenesCompraQuery` (con filtros: estado, sub-estados, proveedor, comprador, fecha, monto, contenedor, ruta, semana) | `compras.ordenes.leer` |
| `ListarPendientesAutorizacionOcQuery` | `compras.ordenes.autorizar.nivel1` o `:nivel2` |
| `ListarPartidasAbiertasQuery` (vista crítica §8.2) | `compras.ordenes.leer` |
| `ListarRequisicionesDisponiblesParaConsolidarQuery` (filtro por sucursal) | `compras.ordenes.crear` |
| `ListarUltimas100ComprasMaterialQuery` | `compras.ordenes.leer` o `compras.requisiciones.leer` |
| `ObtenerArbolDocumentosQuery` (RQ → OC → Recepción → Factura → Pago) | `compras.ordenes.leer` |
| `ObtenerOrdenCompraOrigenQuery` (si la OC viene de `DuplicarOrdenCompra`, navegar a la cancelada/rechazada origen) | `compras.ordenes.leer` |
| `ObtenerHistoricoOrdenCompraQuery` (timeline cronológico completo: creación, transmisión, autorizaciones, recepciones, facturas, pagos, cancelación, duplicación — alimenta tab "Historial" del frontend) | `compras.ordenes.leer` |
| `GenerarPdfOrdenCompraQuery` (transformación de presentación, agrupa por artículo) | `compras.ordenes.leer` |

---

## 7. Reglas y validaciones

### 7.1 Validaciones obligatorias (recopiladas del mapa funcional §6.1)

- Al menos un renglón con `Cantidad > 0` y `PrecioUnitario > 0`.
- `ProveedorId` referencia a proveedor `Activo` (validado al
  enviar a autorización y al autorizar — C10).
- `ArticuloId` referencia a artículo `Activo`.
- Cotización adjunta antes de enviar a autorización, salvo
  `CotizacionExcepcionada == true` + adjunto `correo_autorizacion`
  (C11).
- Si `SinRequisicionPrevia == true`: `MotivoSinRequisicion` no nulo
  + adjunto `correo_autorizacion`.
- Si `EsImportacion == true`: adjunto `ficha_tecnica` antes de
  autorizar; `InformacionImportacion` con todos los campos requeridos.
- Una RQ comprometida en otra OC activa no puede agregarse (C9).
- Restricción de sucursal única (decisión §10.5 cerrada): todas las
  RQs en una OC deben tener `SucursalDestinoId = OC.SucursalDestinoId`.

### 7.2 Validaciones financieras

- IVA por línea: calculado en el momento del cálculo de totales según
  régimen del proveedor + régimen del artículo (C3).
- Retenciones ISR: aplicadas cuando régimen del proveedor las requiere.
- `DescuentoGlobal.Tipo = Porcentaje` ⇒ `0 ≤ Valor ≤ 100`.
- `GastosAdicionales >= 0`.
- `TotalAPagar = BaseGravable + IvaTotal - RetencionIsrTotal +
  Redondeo` (computed).
- `TipoCambio > 0` requerido si `Moneda != MXN`.

### 7.3 Restricciones operativas

- No se acepta captura de OCs durante cierre de mes salvo urgencias
  con bandera y autorización registrada — `[Asunción C13]`: la bandera
  vive como flag de sesión administrado por Contabilidad (vía endpoint
  fuera de este submódulo); este submódulo solo lo consulta.
- Una OC `Autorizada` no se modifica. Para cambios: Cancelar + Duplicar (C4).
- No se puede cancelar una OC con factura asociada en `SubEstadoFacturacion =
  Parcial` o `Completa` sin antes ajustar la factura desde CxP.
- La validación de stock al capturar líneas es **informativa, no
  bloqueante** (el comprador ya validó stock al revisar la RQ).
  Se reusa el puerto `IConsultarStockPort` de Requisiciones.

---

## 8. Integraciones

### 8.1 Con Requisiciones (mismo BC)

- **Entrada:** consume RQs autorizadas vía
  `ListarRequisicionesDisponiblesParaConsolidarQuery`.
- **Compromiso:** al agregar una línea con FK a una RQ, emite
  `RqComprometidaEnOcEvent`. El submódulo de Requisiciones (mismo
  agregado) actualiza `comprometida_en_oc_id` en el agregado
  `Requisicion`.
- **Liberación:** al eliminar una línea o cancelar una OC en
  `Borrador`/`Autorizada` (sin recepciones), emite
  `LineaRqLiberadaEvent` / `OrdenCompraCanceladaEvent` que libera
  `comprometida_en_oc_id`.

> **Decisión clave:** ambos submódulos viven en el mismo BC (`Compras`),
> en el mismo proyecto .NET, contra el mismo `ComprasDbContext`. Las
> integraciones son **in-process via MediatR `INotification`**, no
> Outbox/Service Bus. Esto simplifica la consistencia.

### 8.2 Con Almacén (Recepción)

- **Salida:** la OC `Autorizada` queda visible en la bandeja del
  submódulo de Recepción de Almacén.
- **Entrada:** este submódulo **se suscribe** a `OcRecepcionRegistradaEvent`
  (ya definido como contrato en
  `Compras.Domain.Ports.OrdenCompra.OcRecepcionRegistradaEvent`). El
  listener actualiza `CantidadRecibida` y recalcula
  `SubEstadoRecepcion`.
- **Salida (informativa):** cuando una OC pasa a `Cerrada`, emite
  `OcCerradaEvent` (también contrato existente). Hoy Requisiciones lo
  escucha para registrar cierre de cubrimiento.

> **Decisión cross-BC:** Almacén es un módulo separado (cuando exista).
> La integración es **vía Service Bus (Outbox)** según ADR 0009. En
> ausencia de Almacén real, se mantienen los stubs `NoOp` actuales
> hasta el wiring (ver §12).

### 8.3 Con CxP (Factura de proveedor)

- **Salida:** la OC `Autorizada` permite a CxP crear una o más
  facturas asociadas.
- **Entrada:** el submódulo escucha `FacturaProveedorRegistradaEvent`
  (contrato cerrado en `cuentas-por-pagar/01-diseno.md` §8.1). El
  listener actualiza `CantidadFacturada` por línea y recalcula
  `SubEstadoFacturacion`. **Wireado vía Service Bus en PR #288**
  (`CxpEventListenerWorker` en Compras, topic
  `cuentas-por-pagar-events`, subscription `compras-subscription`); el
  evento llega con `lineas_acumuladas_oc[]` ya calculado por CxP, de
  modo que el handler pasa el acumulado directo a
  `OrdenCompra.RegistrarFacturacionLinea` sin proyección espejo.
- **Pendiente:** `NotaCreditoProveedorRegistradaEvent` se loggea como
  informativo; la NC en CxP no tiene granularidad por línea (rediseño
  necesario en CxP). PLATFORM-TODO `<NcGranularidadLineaOc>`.

### 8.4 Con Tesorería (Pago)

- **Read-only:** este submódulo no opera pagos.
- **Entrada:** escucha `PagoFacturaProveedorEvent` (contrato definido
  en `Compras.Domain.Ports.Tesoreria`). Actualiza `SubEstadoPago`.
- **Pendiente:** el listener `PagoFacturaProveedorListener` queda
  huérfano hasta que se construya `TesoreriaEventListenerWorker` en
  Compras (topic `tesoreria-events`, otra subscription). Requiere
  proyección local de facturas porque Tesorería es módulo externo y su
  payload no incluye `oc_id` ni acumulado. PLATFORM-TODO
  `<TesoreriaEventListenerCompras>` — PR separado.

### 8.5 Eventos de integración emitidos

| Evento | Cuándo se emite | Suscriptores esperados |
|---|---|---|
| `OrdenCompraEnviadaAAutorizacionEvent` | Tras `EnviarAAutorizacion()` | Notificaciones (email a N1) |
| `OrdenCompraAutorizadaEvent` | Tras `Autorizar(N2)` exitoso | Notificaciones, Servicio PDF, Recepción (visibilidad) |
| `OrdenCompraRechazadaEvent` | Tras `Rechazar` en cualquier nivel | Notificaciones (email a creador) |
| `OrdenCompraCanceladaEvent` | Tras `Cancelar()` | Requisiciones (libera RQs), Almacén, CxP |
| `OrdenCompraCerradaEvent` | Cuando 3 sub-estados llegan a Completa/Completa/Pagada | Requisiciones (cubrimiento), reportes |
| `OrdenCompraDuplicadaEvent` | Tras `DuplicarOrdenCompra()` | Auditoría (vincula OC nueva ↔ OC origen) |
| `LineaRqLiberadaEvent` | Tras quitar línea o cancelar OC en Borrador | Requisiciones (mismo BC) |
| `RqComprometidaEnOcEvent` | Tras agregar línea con FK a RQ | Requisiciones (mismo BC) |

### 8.6 Eventos de integración consumidos

| Evento | Origen | Efecto |
|---|---|---|
| `OcRecepcionRegistradaEvent` | Almacén | Actualiza `CantidadRecibida` y `SubEstadoRecepcion`. Si trae flag `factura_pendiente=true` (variante B materiales directos), no incrementa `SubEstadoFacturacion` hasta recibir `FacturaProveedorRegistradaEvent`. |
| `OcDevolucionRegistradaEvent` | Almacén | Decrementa `CantidadRecibida`. Cierra la devolución a proveedor del sub-flujo 8.B de Almacén §8.4 (referenciada en CxP por evento separado). |
| `FacturaProveedorRegistradaEvent` | CxP | Actualiza `CantidadFacturada` y `SubEstadoFacturacion`. **Wireado en PR #288** vía `CxpEventListenerWorker` en Compras; el evento llega con `lineas_acumuladas_oc[]` ya calculado en CxP. |
| `FacturaProveedorRechazadaPorToleranciaEvent` | CxP | Marca la factura como rechazada en el log de la OC con motivo y diferencia detectada. Activa bandera "OC requiere corrección" para Compras. |
| `FacturaProveedorCanceladaEvent` | CxP | Si la factura estaba contada en `CantidadFacturada`, la decrementa. |
| `NotaCreditoProveedorRegistradaEvent` | CxP | Decrementa `CantidadFacturada`. |
| `DiferenciaPrecioFacturaDetectadaEvent` | CxP | **Informativo** para OC en variante B: registra que el precio de la factura difiere del de la OC dentro de tolerancia. No cambia sub-estados de OC. (El ajuste de costo de inventario lo aplica Almacén; el ajuste de pasivo lo registra CxP.) |
| `PagoFacturaProveedorEvent` | Tesorería | Actualiza `SubEstadoPago`. |

### 8.7 Catálogos consumidos

| Catálogo | Fuente | Modo de uso |
|---|---|---|
| Proveedores | `compartido.proveedores` | FK + snapshot de contacto |
| Artículos | `compartido.articulos` | FK + snapshot de UM y descripción |
| Condiciones de pago | `compartido.condiciones_pago` | FK |
| Monedas y T/C | `compartido.monedas` | FK |
| Almacenes | `compartido.almacenes` | FK |
| Unidades de medida | `compartido.unidades_medida` | snapshot |
| Departamentos | `compartido.departamentos` | FK |
| Sucursales | `compartido.sucursales` | FK |
| Regímenes fiscales | `compartido.regimenes_fiscales` | lookup para C3 |
| Incoterms | `compartido.incoterms` | FK (solo importaciones) |
| Transportistas | `compartido.transportistas` | FK (opcional, también texto libre) |
| Tipos de documento OC | `compras.tipos_documento_oc` | seed propio (C6) |
| Motivos de rechazo | `compras.motivos_rechazo` | reusa de Requisiciones |

---

## 9. Permisos canónicos

Siguen el patrón de Requisiciones (`compras.requisiciones.*`). Se
declaran como constantes en `Identidad/Domain/PermisosCanonicos.cs`
con GUIDs deterministas (namespace `00000004-...` reservado para OC).

| Permiso | Descripción |
|---|---|
| `compras.ordenes.leer` | Ver bandeja, detalle, reportes. |
| `compras.ordenes.crear` | Crear, editar, transmitir, duplicar (desde una OC cancelada/rechazada). |
| `compras.ordenes.crear.sin_rq` | Crear OC sin requisición previa (caso especial §4.3 del mapa funcional — solo Dirección y compradores designados). |
| `compras.ordenes.adjuntar` | Adjuntar/quitar documentos. |
| `compras.ordenes.logistica` | Editar información logística post-autorización (transportista, guía, pedimento). |
| `compras.ordenes.autorizar.nivel1` | Autorizar / rechazar como Jefe de Compras. |
| `compras.ordenes.autorizar.nivel2` | Autorizar / rechazar como Dirección. |
| `compras.ordenes.cancelar` | Cancelar OCs en Borrador o sin recepciones. |
| `compras.ordenes.cancelar.doble` | Cancelar OCs con recepciones (requiere también `.nivel1` + `.nivel2`). |
| `compras.ordenes.reportes.partidas_abiertas` | Ver el reporte de partidas abiertas (vista crítica). |

> **Asignación a roles:** se cubre en el seed inicial (Fase 0.b
> equivalente del OC submódulo). Mapeo tentativo: Comprador →
> `.leer`, `.crear`, `.adjuntar`, `.cancelar`, `.reportes.*`. Jefe de
> Compras → todo lo del Comprador + `.autorizar.nivel1`, `.logistica`.
> Director → `.autorizar.nivel2`, `.cancelar.doble`.

---

## 10. Modelo de datos

### 10.1 Tablas principales (esquema `compras`)

```sql
compras.ordenes_compra (
    id                          uuid          PK,
    empresa_id                  uuid          NOT NULL,
    folio                       varchar(40)   NOT NULL,
    referencia_proveedor        varchar(60),
    proveedor_id                uuid          NOT NULL,
    contacto_proveedor_nombre   varchar(200),
    contacto_proveedor_email    varchar(200),
    contacto_proveedor_telefono varchar(50),
    sucursal_destino_id         uuid          NOT NULL,
    almacen_destino_default_id  uuid          NOT NULL,
    moneda                      char(3)       NOT NULL DEFAULT 'MXN',
    tipo_cambio                 numeric(18,6),
    condiciones_pago_id         uuid          NOT NULL,
    uso_principal_id            uuid          NOT NULL,
    encargado_compras_id        uuid          NOT NULL,
    comprador_titular_id        uuid          NOT NULL,
    observaciones               text,
    sin_requisicion_previa      boolean       NOT NULL DEFAULT false,
    es_importacion              boolean       NOT NULL DEFAULT false,
    cotizacion_excepcionada     boolean       NOT NULL DEFAULT false,
    fecha_documento             timestamptz   NOT NULL,
    fecha_contabilizacion       timestamptz,
    fecha_entrega_esperada      timestamptz,
    estado                      smallint      NOT NULL,
    sub_estado_recepcion        smallint      NOT NULL DEFAULT 0,
    sub_estado_facturacion      smallint      NOT NULL DEFAULT 0,
    sub_estado_pago             smallint      NOT NULL DEFAULT 0,
    info_logistica_direccion    text,
    info_logistica_transportista_id uuid,
    info_logistica_transportista_texto varchar(200),
    info_logistica_numero_guia  varchar(80),
    info_logistica_instrucciones text,
    info_import_incoterm_id     uuid,
    info_import_pais_origen     char(2),
    info_import_numero_contenedor varchar(40),
    info_import_codigo_ruta     varchar(40),
    info_import_semana_embarque varchar(40),
    info_import_numero_pedimento varchar(60),
    descuento_global_tipo       smallint,         -- nullable
    descuento_global_valor      numeric(15,4),
    gastos_adicionales          numeric(15,2)     NOT NULL DEFAULT 0,
    redondeo                    numeric(15,2)     NOT NULL DEFAULT 0,
    motivo_sin_requisicion      text,
    motivo_cancelacion          text,
    motivo_rechazo_id           uuid,
    motivo_rechazo_texto        text,
    oc_origen_id                uuid              REFERENCES compras.ordenes_compra(id),  -- C4: trazabilidad si fue duplicada
    version                     bigint            NOT NULL DEFAULT 0,
    created_at                  timestamptz       NOT NULL,
    created_by                  uuid              NOT NULL,
    updated_at                  timestamptz       NOT NULL,
    updated_by                  uuid              NOT NULL,
    deleted_at                  timestamptz,
    CONSTRAINT uq_ordenes_compra_folio UNIQUE (empresa_id, sucursal_destino_id, folio),
    CONSTRAINT ck_oc_tipo_cambio CHECK (
      (moneda = 'MXN' AND tipo_cambio IS NULL) OR
      (moneda <> 'MXN' AND tipo_cambio > 0)
    ),
    CONSTRAINT ck_oc_import_campos CHECK (
      (es_importacion = false) OR
      (es_importacion = true AND info_import_incoterm_id IS NOT NULL
       AND info_import_pais_origen IS NOT NULL
       AND info_import_numero_contenedor IS NOT NULL)
    ),
    CONSTRAINT ck_oc_sin_rq_motivo CHECK (
      sin_requisicion_previa = false OR motivo_sin_requisicion IS NOT NULL
    )
);

CREATE INDEX ix_oc_empresa_estado ON compras.ordenes_compra (empresa_id, estado);
CREATE INDEX ix_oc_empresa_proveedor ON compras.ordenes_compra (empresa_id, proveedor_id);
CREATE INDEX ix_oc_empresa_comprador ON compras.ordenes_compra (empresa_id, comprador_titular_id);
CREATE INDEX ix_oc_empresa_fecha ON compras.ordenes_compra (empresa_id, fecha_documento DESC);
CREATE INDEX ix_oc_referencia_proveedor ON compras.ordenes_compra (empresa_id, referencia_proveedor)
  WHERE referencia_proveedor IS NOT NULL;
CREATE INDEX ix_oc_partidas_abiertas ON compras.ordenes_compra
  (empresa_id, estado, sub_estado_recepcion, sub_estado_facturacion, sub_estado_pago)
  WHERE estado = 3;  -- Autorizada
CREATE INDEX ix_oc_contenedor ON compras.ordenes_compra (info_import_numero_contenedor)
  WHERE info_import_numero_contenedor IS NOT NULL;
CREATE INDEX ix_oc_ruta ON compras.ordenes_compra (info_import_codigo_ruta)
  WHERE info_import_codigo_ruta IS NOT NULL;
CREATE INDEX ix_oc_semana ON compras.ordenes_compra (info_import_semana_embarque)
  WHERE info_import_semana_embarque IS NOT NULL;
CREATE INDEX ix_oc_origen ON compras.ordenes_compra (oc_origen_id)
  WHERE oc_origen_id IS NOT NULL;  -- C4: trazabilidad de duplicación
```

```sql
compras.orden_compra_lineas (
    id                          uuid          PK,
    orden_compra_id             uuid          NOT NULL REFERENCES compras.ordenes_compra(id) ON DELETE CASCADE,
    posicion                    int           NOT NULL,
    articulo_id                 uuid          NOT NULL,
    descripcion_extendida       text,
    cantidad                    numeric(15,4) NOT NULL CHECK (cantidad > 0),
    unidad_medida               varchar(20)   NOT NULL,
    precio_unitario             numeric(15,4) NOT NULL CHECK (precio_unitario >= 0),
    descuento_tipo              smallint,
    descuento_valor             numeric(15,4),
    indicador_impuestos         varchar(40)   NOT NULL,
    iva_importe                 numeric(15,2) NOT NULL DEFAULT 0,
    retencion_isr               numeric(15,2),
    almacen_destino_id          uuid          NOT NULL,
    departamento_solicitante_id uuid          NOT NULL,
    requisicion_id              uuid,
    linea_requisicion_id        uuid,
    fecha_entrega_linea         timestamptz,
    cantidad_recibida           numeric(15,4) NOT NULL DEFAULT 0 CHECK (cantidad_recibida >= 0),
    cantidad_facturada          numeric(15,4) NOT NULL DEFAULT 0 CHECK (cantidad_facturada >= 0),
    texto_adicional             text,
    version                     bigint        NOT NULL DEFAULT 0,
    created_at                  timestamptz   NOT NULL,
    updated_at                  timestamptz   NOT NULL,
    CONSTRAINT uq_oc_lineas_posicion UNIQUE (orden_compra_id, posicion),
    CONSTRAINT ck_oc_lineas_recibida_max CHECK (cantidad_recibida <= cantidad),
    CONSTRAINT ck_oc_lineas_facturada_max CHECK (cantidad_facturada <= cantidad),
    CONSTRAINT ck_oc_lineas_rq_coherente CHECK (
      (requisicion_id IS NULL AND linea_requisicion_id IS NULL) OR
      (requisicion_id IS NOT NULL AND linea_requisicion_id IS NOT NULL)
    )
);

CREATE INDEX ix_oc_lineas_oc ON compras.orden_compra_lineas (orden_compra_id);
CREATE INDEX ix_oc_lineas_articulo ON compras.orden_compra_lineas (articulo_id);
CREATE INDEX ix_oc_lineas_rq ON compras.orden_compra_lineas (requisicion_id)
  WHERE requisicion_id IS NOT NULL;
CREATE INDEX ix_oc_lineas_almacen ON compras.orden_compra_lineas (almacen_destino_id);
```

```sql
compras.orden_compra_autorizaciones (
    id                  uuid          PK,
    orden_compra_id     uuid          NOT NULL REFERENCES compras.ordenes_compra(id) ON DELETE CASCADE,
    nivel               smallint      NOT NULL,    -- 1 = JefeCompras, 2 = Direccion
    resultado           smallint      NOT NULL,    -- 1 = Autorizado, 2 = Rechazado
    usuario_id          uuid          NOT NULL,
    fecha_hora          timestamptz   NOT NULL,
    motivo_rechazo_id   uuid,
    motivo_rechazo_texto text,
    notas               text,
    CONSTRAINT ck_oc_autorizaciones_motivo CHECK (
      resultado = 1 OR (resultado = 2 AND motivo_rechazo_id IS NOT NULL)
    )
);

CREATE UNIQUE INDEX uq_oc_autorizaciones_autorizado
  ON compras.orden_compra_autorizaciones (orden_compra_id, nivel)
  WHERE resultado = 1;
CREATE INDEX ix_oc_autorizaciones_usuario ON compras.orden_compra_autorizaciones (usuario_id);
```

```sql
compras.orden_compra_adjuntos (
    id                  uuid          PK,
    orden_compra_id     uuid          NOT NULL REFERENCES compras.ordenes_compra(id) ON DELETE CASCADE,
    tipo_documento_id   uuid          NOT NULL REFERENCES compras.tipos_documento_oc(id),
    nombre_archivo      varchar(255)  NOT NULL,
    blob_url            text          NOT NULL,
    content_type        varchar(120)  NOT NULL,
    tamano_bytes        bigint        NOT NULL,
    fecha_carga         timestamptz   NOT NULL,
    usuario_carga_id    uuid          NOT NULL
);

CREATE INDEX ix_oc_adjuntos_oc ON compras.orden_compra_adjuntos (orden_compra_id);
CREATE INDEX ix_oc_adjuntos_tipo ON compras.orden_compra_adjuntos (tipo_documento_id);
```

```sql
compras.tipos_documento_oc (
    id                              uuid          PK,
    clave                           varchar(40)   UNIQUE NOT NULL,
    descripcion                     varchar(200)  NOT NULL,
    obligatorio_si_importacion      boolean       NOT NULL DEFAULT false,
    activo                          boolean       NOT NULL DEFAULT true
);
```

```sql
compras.folio_secuencias_oc (
    empresa_id          uuid          NOT NULL,
    sucursal_id         uuid          NOT NULL,
    anio                int           NOT NULL,
    siguiente           bigint        NOT NULL DEFAULT 1,
    PRIMARY KEY (empresa_id, sucursal_id, anio)
);
```

### 10.2 Modificaciones a tablas existentes

```sql
ALTER TABLE compras.requisiciones
  ADD COLUMN comprometida_en_oc_id uuid REFERENCES compras.ordenes_compra(id);

CREATE INDEX ix_requisiciones_comprometida
  ON compras.requisiciones (empresa_id, comprometida_en_oc_id)
  WHERE comprometida_en_oc_id IS NOT NULL;

CREATE INDEX ix_requisiciones_disponibles
  ON compras.requisiciones (empresa_id, sucursal_id, estado)
  WHERE comprometida_en_oc_id IS NULL;
```

### 10.3 Índices clave (resumen)

| Índice | Propósito |
|---|---|
| `ix_oc_empresa_estado` | Bandejas por estado |
| `ix_oc_referencia_proveedor` | Búsqueda por folio externo del proveedor (mapa funcional §5.1) |
| `ix_oc_partidas_abiertas` | Reporte crítico §8.2 del mapa funcional |
| `ix_oc_contenedor`, `ix_oc_ruta`, `ix_oc_semana` | Filtros de importaciones |
| `ix_oc_lineas_rq` | Árbol de documentos (§8.4 mapa funcional) |
| `ix_requisiciones_disponibles` | Selector de consolidación (§4.2 mapa funcional) |

---

## 11. Estrategia de catálogos en MVP

En MVP los catálogos maestros que consume OC se operan con **datos seed
versionados** con el código + endpoints `GET` **read-only**. **No hay
pantallas de CRUD ni importación desde SAP** en este alcance (decisión
cerrada — mapa funcional §10 "Decisiones resueltas").

### 11.1 Catálogos disponibles para OC en MVP

| Catálogo | Origen del seed | Tabla |
|---|---|---|
| Proveedores | Heredado de RQ (seed existente) | `compartido.proveedores` |
| Artículos | Heredado de RQ (seed existente) | `compartido.articulos` |
| Sucursales | Heredado de RQ (seed existente) | `compartido.sucursales` |
| Almacenes | Heredado de RQ (seed existente) | `compartido.almacenes` |
| Departamentos | Heredado de RQ (seed existente) | `compartido.departamentos` |
| Unidades de medida | Heredado de RQ (seed existente) | `compartido.unidades_medida` |
| Monedas | Heredado de RQ (seed existente) | `compartido.monedas` |
| Condiciones de pago | Nuevo seed para OC | `compartido.condiciones_pago` |
| Incoterms | Nuevo seed para OC (solo importaciones) | `compartido.incoterms` |
| Transportistas | Nuevo seed para OC | `compartido.transportistas` |
| Regímenes fiscales | Nuevo seed para OC (cálculo de IVA y retenciones) | `compartido.regimenes_fiscales` |
| Tipos de documento OC | Nuevo seed para OC | `compras.tipos_documento_oc` |
| Motivos de rechazo | Heredado de RQ con extensión bitmask | `compras.motivos_rechazo` |

Los seeds se versionan con migraciones EF Core (`HasData`) o vía
`IHostedService` de seeding que se autoexcluye en `Production`. Los
endpoints `GET /api/v1/catalogos/...` viven en el módulo de Datos
Maestros (ya implementado para RQ, se extiende para los catálogos
nuevos de OC). Permisos: `compartido.catalogos.leer`.

### 11.2 Diferido post-MVP

Como bloque separado posterior al go-live de OC:

- **Pantallas de administración (CRUD)** de los catálogos maestros.
- **Importación inicial desde SAP B1** de proveedores, artículos y
  catálogos relacionados (cuando exista, convierte al ERP en master
  operativo y deja SAP obsoleto para estos catálogos).
- **Importación de OCs históricas** si el cliente la requiere (no es
  pre-requisito de operación del MVP — el portal/SAP legacy queda
  como sistema de consulta histórica, similar al patrón aplicado a
  Requisiciones).

### 11.3 Lo que el modelo permite cuando se reabra

Cuando se construya el bloque post-MVP, el modelo de dominio actual
soporta migración **aditiva** sin reestructuración:

- Ingerir OCs históricas mapeando estados SAP → estados de este
  agregado.
- Preservar folio interno SAP en `folio` (o usar prefijo de
  diferenciación).
- Preservar campos legacy de logística (RUTA/CONTENEDOR/SEMANA
  embebidos en comentarios) parseando heurísticamente al estructurado.
- Mantener referencia a OC origen en SAP en columna nueva opcional
  `id_externo_sap` (migración aditiva si se reabre).

---

## 12. Dependencias de plataforma pendientes

Convención ADR-0031. Esta sección lista las piezas de infraestructura
compartida que este submódulo asume pero que **aún no existen** o
**están parcialmente implementadas**. Cada `NoOp` o stub debe llevar
un `// PLATFORM-TODO(<id>): ...` en código.

| Pieza | Ticket / id | `NoOp` en uso | Cómo se wirea |
|---|---|---|---|
| **`CollaborationHub` SignalR (ADR 0012 Capa 2)** | `<CollaborationHub>` | Heredado del módulo: lista de entidades con soft lock en `Compras.Infrastructure.Collaboration`. Se agrega `OrdenCompra` a la lista pero el hub real no existe. | Cuando el hub se construya, declarar `OrdenCompra` y `useCollaboration("orden-compra", id)` en frontend funcionará out-of-the-box. |
| ~~**Outbox + Service Bus (ADR 0009)**~~ | ~~`<Outbox>`~~ | **CERRADO en F8-PR1 (2026-05-12)**: el Outbox real se introdujo por F6-PR1+PR2 de Requisiciones y OC publica sus 6 integration events versionados (`compras.orden-compra.*.v1`) por el mismo cable. Ver `docs/integraciones/compras-eventos.md`. | — |
| **Módulo Almacén (Recepción)** | `<Almacen.Recepcion>` | Hoy los puertos `IGenerarMovimientoSalidaPort`, `IReservarStockPort`, `IConsultarStockPort` son stubs (`InMemoryGenerarMovimientoSalidaPort`, etc.). Para OC: el evento `OcRecepcionRegistradaEvent` lo emite hoy el `OcBorradorStub`; cuando exista Recepción, lo emite el módulo real. | En el momento que Recepción exista, este submódulo retira el stub y conecta vía Outbox/Service Bus o como módulo separado del monolito. |
| ~~**Módulo CxP (Factura de proveedor) — `FacturaProveedorRegistradaEvent`**~~ | ~~`<CxP.Factura>`~~ | **CERRADO en PR #288 (2026-05-24)**: `CxpEventListenerWorker` en Compras consume `cuentas_por_pagar.factura.registrada.v1` con `lineas_acumuladas_oc[]` ya calculado por CxP; sub-estado Facturación de OC avanza automáticamente. | — |
| **Módulo CxP — `NotaCreditoProveedorRegistradaEvent` granular por línea OC** | `<NcGranularidadLineaOc>` | El listener `NotaCreditoProveedorRegistradaListener` en Compras está escrito pero queda huérfano: la NC en CxP no tiene granularidad por línea (solo `Total`). El worker loggea el evento como informativo. | Rediseño de modelo NC en CxP para incluir líneas con `linea_oc_id`, o decisión explícita de prorrateo por línea. |
| **Módulo Tesorería (Pago) — `PagoFacturaProveedorEvent`** | `<TesoreriaEventListenerCompras>` | El listener `PagoFacturaProveedorListener` está escrito pero queda huérfano: no hay worker SB en Compras suscrito a `tesoreria-events`. El payload externo no incluye `oc_id` ni acumulado. | Crear `TesoreriaEventListenerWorker` en Compras + proyección local `facturas_proveedor_local` (mapping `factura_id → oc_id`) + proyección de pagos por OC para calcular `MontoPagadoAcumulado`. PR separado. |
| ~~**Servicio de PDF institucional**~~ | ~~`<PdfService>`~~ | **CERRADO en F6-PR3 (2026-05-12)**: `QuestPdfOrdenCompraGenerator` reemplazó al stub con layout institucional real (QuestPDF Community license, válida hasta $1M revenue). | — |
| **Módulo Notificaciones** | `<Notificaciones>` | Inexistente. Los eventos `OrdenCompraEnviadaAAutorizacionEvent`, `OrdenCompraAutorizadaEvent`, `OrdenCompraRechazadaEvent` se publican pero no envían email. | ADR 0026: cuando exista, el módulo Notificaciones se suscribe y envía email. |
| **Servicio de tipo de cambio (C1)** | `<TipoCambio>` | Inexistente. v1 acepta T/C manual; no hay puerto. | Si se decide automatizar (C1 "no"), se agrega `ITipoCambioPort` con stub apuntando a captura manual. |
| **Catálogos maestros (CRUD + migración SAP)** | `<DatosMaestros>` | Heredado del módulo `Datos Maestros` separado. Hoy y durante todo el MVP los catálogos viven de seeds versionados + endpoints GET read-only. | **Bloque post-MVP** (no parte del scope de OC v1): pantallas de CRUD + ejecución de migración inicial desde SAP. |

**Checklist en cada ticket de plataforma:** este submódulo (OC) entra
como **consumidor** en los tickets de `<Almacen.Recepcion>`,
`<NcGranularidadLineaOc>`, `<TesoreriaEventListenerCompras>`,
`<PdfService>` y `<Notificaciones>`.

---

## 13. Riesgos y decisiones diferidas

### 13.1 Riesgos técnicos

| Riesgo | Probabilidad | Mitigación |
|---|---|---|
| Sub-estados materializados desincronizados con la realidad (lineas) | Media | Tests de integración que validen consistencia tras cada listener; reconciliación nocturna como background job (post-v1). |
| Compromiso de RQs deja huérfanas si una OC se elimina hard-delete | Baja | El agregado nunca hace hard delete; cancelación es soft con motivo. Migraciones que toquen OC deben preservar la integridad referencial. |
| Listener de `OcRecepcionRegistradaEvent` falla y la OC no avanza | Media | Outbox garantiza at-least-once; idempotencia en el listener (`cantidad_recibida` es un set, no un incremento del payload). |
| Uso excesivo de "Cancelar + Duplicar" (C4) como atajo para evitar planificación | Baja | KPI mensual de OCs canceladas con `OcDuplicada` derivado vinculado, por comprador. Si supera umbral acordado, revisar adherencia al workflow. |
| Restricción de sucursal única bloquea consolidaciones legítimas | Baja | Decisión cerrada (§10.5); revisar con Rodrigo si aparecen casos en producción. |

### 13.2 Decisiones diferidas

| Decisión | Diferida a | Razón |
|---|---|---|
| Cliente final destinatario | Fase 2 (C5) | Sub-decisiones de master y modo de referencia (§10.4 mapa funcional). |
| **Importación inicial desde SAP B1** (catálogos + OCs históricas) | **post-MVP**, bloque separado | MVP opera con seeds versionados read-only; la migración se reabre cuando se construyan las pantallas de CRUD de Datos Maestros. |
| Pantallas CRUD de catálogos maestros | post-MVP, bloque separado | Coordinado con el módulo Datos Maestros — no scope de OC v1. |
| Servicio de T/C automático (Banxico) | post-v1 | C1 mantiene T/C manual hasta integración. |
| Contratos marco / OCs abiertas para servicios recurrentes | post-v1 | Mapa funcional §9.4. |
| Portal de proveedores | fuera de scope | Decisión cerrada. |
| Validación CFDI vs SAT | submódulo Factura de Proveedor | No mencionar aquí (decisión §10.1 cerrada). |
| Modo offline | infraestructura | No mencionar aquí (decisión §10.2 cerrada). |
| Bulk operations (autorización masiva) | v1.1 | Patrón validado en Requisiciones. |
| Vista materializada de partidas abiertas | post-v1 | Solo si la query nativa degrada bajo carga real. |

---

## 14. Reutilización de código (backend + frontend)

> **Regla del proyecto (memoria):** antes de proponer código nuevo,
> identificar qué piezas existen y se pueden reusar tal cual o extender.
> Esta sección consolida las decisiones de reuso del submódulo OC sobre
> la infraestructura ya construida por **Requisiciones** (exemplar del
> patrón cross-módulo del ERP) y el **shell** común.

### 14.1 Backend — heredado tal cual (sin tocar)

Estas piezas ya existen en el repo; OC las usa por referencia o
inyección, no se reescriben.

| Pieza | Ubicación | Cómo la usa OC |
|---|---|---|
| `BaseEntity` con `Version IsConcurrencyToken` | `backend/src/SharedKernel/Domain/BaseEntity.cs` | `OrdenCompra` hereda. Concurrencia Capa 1 gratis. |
| `BaseDbContext` + interceptors (Audit, Empresa, Metadata) | `backend/src/SharedKernel/Persistence/` | `ComprasDbContext` ya hereda; OC se agrega como `DbSet<OrdenCompra>`. Auditoría e idempotencia funcionan sin código adicional. |
| `ComprasDbContext` | `backend/src/Compras/Infrastructure/` | Mismo contexto, mismo schema `compras`. No nuevo DbContext (no aplica el checklist de "DbContext nuevo"). |
| `Money`, `Moneda`, `Empresa` (VOs) | `backend/src/SharedKernel/Domain/` | `LineaOrdenCompra.PrecioUnitario`, `TotalesOC.*` usan `Money`. |
| `IClock`, `ICurrentUserContext`, `ICurrentEmpresaContext` | `backend/src/SharedKernel/Abstractions/` | Inyectados en handlers de OC. |
| Pipeline MediatR + FluentValidation + Mapster | wiring en `Program.cs` | Patron `Command → Validator → Handler → Response`. Sin nueva configuración. |
| `RequirePermissionAttribute` + `PermissionAuthorizationHandler` | `backend/src/Api/Auth/` | Endpoints de OC anotados con `[RequirePermission("compras.ordenes.*")]`. |
| `GlobalExceptionHandler` (Problem Details RFC 7807) | `backend/src/Api/Web/` | Errores de OC se devuelven en el mismo formato. |
| `IFiscalmenteRelevante` (soft delete) | `backend/src/SharedKernel/Domain/` | `OrdenCompra` lo implementa; `BaseDbContext` filtra soft-deleted automáticamente. |
| `MigrationsAppliedHealthCheck` | `backend/src/Api/Health/` | Migraciones de OC se cubren automáticamente. |
| Serilog + `MaskSensitivePropertiesEnricher` | wiring en `Program.cs` | Logging estructurado funciona sin código nuevo. |
| ETag pattern (basado en `Version`) | `backend/src/Compras/Application/ObtenerRequisicionPorId/` | Mismo patrón en `ObtenerOrdenCompraPorId`. |
| Idempotency-Key middleware (ADR 0020) | `backend/src/Api/Web/` | POST de creación de OC lo consume sin código nuevo. |
| `IConsultarStockPort` (consulta informativa) | `backend/src/Compras/Domain/Ports/Almacen/IConsultarStockPort.cs` | Reusa para "Existencia actual del material" en captura de línea de OC. |
| Stubs InMemory para puertos cross-BC | `backend/src/Compras/Infrastructure/Stubs/` | `InMemoryConsultarStockPort` sigue sirviendo. |
| `ComprasStubsServiceCollectionExtensions` | `backend/src/Compras/Infrastructure/Stubs/` | Se extiende con los nuevos stubs de OC (PDF, recepción, factura, pago, T/C). |

### 14.2 Backend — extender / parametrizar

| Pieza existente | Cambio | Razón |
|---|---|---|
| `MotivoRechazo` catálogo + `MotivoRechazoAplicaA` bitmask | Agregar bit `OrdenCompra = 8` al enum/bitmask y seed de motivos con `aplica_a` cubriendo OC. | Reusa exactamente el motor de motivos de rechazo de RQ. No se crea catálogo separado. |
| `NivelAutorizacion` enum (`Nivel1`, `Nivel2`) | Reusa tal cual. La semántica humana cambia (en OC: Nivel1 = Jefe de Compras, Nivel2 = Dirección) pero el enum no necesita valores nuevos. | Coherencia con RQ. |
| Patrón de `Folio` + secuencia atómica | Nueva tabla `compras.folio_secuencias_oc` siguiendo el mismo patrón de `compras.folio_secuencias` (de RQ). VO `Folio` se parametriza por prefijo (`RQ-` vs `OC-`). | Patrón ya validado; cambio mínimo. |
| `PermisosCanonicos` constantes | Agregar 10 constantes `compras.ordenes.*` con GUIDs deterministas namespace `00000004-...` (RQ usa `00000003-...`). | Mismo patrón que `compras.requisiciones.*`. |
| `compras.requisiciones` (tabla) | ALTER aditivo: `comprometida_en_oc_id uuid NULL` + 2 índices parciales. Nullable, sin impacto en datos existentes. | Modelar compromiso exclusivo (C9 / §3.bis.1). |
| `Requisicion` agregado | Métodos nuevos `ComprometerEnOc(ocId)` y `LiberarDeOc()`. Sin breaking change en API existente. | Cierre del ciclo cuando OC consume/cancela RQs. |
| `compras` schema | Mismas convenciones de naming (snake_case, índices nominados `ix_*`, FKs `fk_*`, uniques `uq_*`, checks `ck_*`). | No reinventar convenciones. |
| Listener pattern (`Application/Eventos/*LoggingHandler.cs`) | OC agrega listeners análogos para sus eventos. | Mismo wiring MediatR + Serilog. |

### 14.3 Backend — nuevo (no había equivalente)

| Pieza nueva | Justificación de por qué no se reusa |
|---|---|
| Agregado `OrdenCompra` | Modelo de dominio propio (cabecera + líneas + autorizaciones + adjuntos + versiones). Comparte patrones con `Requisicion` pero la cardinalidad de campos y reglas no permiten una abstracción común sin sobreingeniería. |
| `LineaOrdenCompra` | Estructura distinta a `LineaRequisicion` (precio unitario, IVA, retención ISR, cantidades recibida/facturada). Compartir base abstracta agregaría poco. |
| VOs `InformacionLogistica`, `InformacionImportacion`, `TotalesOC`, `DescuentoGlobal`, `DescuentoLinea`, `ContactoProveedor`, `ReferenciaProveedor` | Específicos del modelo de OC. |
| `AdjuntoOC` + `tipos_documento_oc` catálogo | RQ v1 no tiene adjuntos (v1.1 sí, según A11 de RQ). OC los necesita en v1 con tipos específicos (cotización, ficha técnica, pedimento). Cuando RQ v1.1 los implemente, **se evalúa promover a componente compartido** `Compras.Domain.Adjuntos` para no duplicar. |
| `OcOrigenId` (trazabilidad de duplicación) | Concepto nuevo (C4 cerrado como cancelar + duplicar). RQ no lo necesita; OC lo usa para vincular una OC nueva con la cancelada/rechazada de la que se duplicó. |
| Sub-estados materializados (`SubEstadoRecepcion`, `SubEstadoFacturacion`, `SubEstadoPago`) | OC específico; RQ no tiene sub-estados independientes (es state machine de 1 dimensión). |
| Listeners cross-BC (`OcRecepcionRegistradaListener`, `FacturaProveedorRegistradaListener`, `PagoFacturaProveedorListener`) | Nuevos contratos, suscripción nueva. |
| Servicio de PDF (`IGenerarPdfOrdenCompraPort` + stub) | No existe servicio PDF compartido en el ERP. OC define el puerto **como contrato cross-módulo** anticipando que CxP, Recepción y otros lo necesitarán. Se promueve a `SharedKernel.Pdf` si tres o más módulos lo consumen. |
| `ITipoCambioPort` (si aplica, según C1) | Hoy T/C es manual; el puerto se reserva como nuevo si se automatiza. |

### 14.4 Frontend — heredado tal cual (shell común)

Estas piezas ya viven en el repo del frontend; OC las consume sin
duplicar. Referencia: [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md).

| Pieza | Ubicación | Cómo la usa OC |
|---|---|---|
| Cliente HTTP enriquecido (`apiRequest`, `ApiError`, ProblemDetails parsing, ETag captura, `useFormIdempotencyKey`, retry 409, `applyServerErrors`) | `frontend/src/lib/api/` | Todos los hooks de read/mutate de OC pasan por aquí. |
| Permisos canónicos en TS | `frontend/src/lib/auth/permission-codes.ts` | Se extiende con `compras.ordenes.*` (sin tocar el resto). |
| Cliente de auth + shell autenticado + sidebar | `frontend/src/routes/_app.tsx`, `frontend/src/lib/nav.ts` | OC agrega un item "Órdenes de compra" bajo el grupo Compras. |
| shadcn primitives (Dialog, Select, Command, Form, Toast, Calendar, Popover, Card, Badge, Skeleton, Alert) | `frontend/src/components/ui/*` | Mismos componentes. |
| `<Toaster>` (sonner) wireado | `_app.tsx` | Mismo toast system. |
| UX kit del módulo Compras: `<EmptyState>`, `<ErrorState>`, `<TableSkeleton>` | `frontend/src/components/erp/feedback/` | Mismas pantallas vacías/error/loading. |
| `<Breadcrumbs>` con preservación de search params | `frontend/src/components/erp/Breadcrumbs.tsx` | "Compras / Órdenes de compra / OC-MID2026-000001". |
| `useUnsavedChangesGuard(isDirty)` | `frontend/src/lib/hooks/` | Confirm al cerrar Sheet o salir de form con cambios. |
| `<DomainTermTooltip>` + glosario | `frontend/src/features/compras/lib/glosario.ts` | Se extiende con términos OC (sub-estados, consolidación, duplicar, contenedor, ruta, semana). |
| `<CollaborationIndicator>` + `useCollaboration` | `frontend/src/components/erp/collaboration/` | `useCollaboration("orden-compra", id)` muestra "Pedro está editando" (stub silente hasta wiring real del hub). |
| `<ConflictResolutionDialog>` con preserve-form-state | `frontend/src/components/erp/collaboration/` | 409 conflicts en mutaciones de OC se manejan idénticamente a RQ. |
| Display components: `<MoneyDisplay>`, `<DateTimeDisplay>` | `frontend/src/components/erp/display/` | Sin cambios. |
| Hooks de read pattern (query keys estructuradas, `meta.etag`) | `frontend/src/features/compras/api/` | Mismo patrón para `useOrdenesCompra`, `useOrdenCompra`, etc. |
| **Estructura de ventanas master-detail + Sheet + inline forms** | `frontend/docs/patrones-compras.md` §6 | Patrón exemplar de OC: bandeja 320px sticky a la izquierda + detalle a la derecha; mobile drill-down; Sheet slide-from-right para "Nueva OC"; inline form con border dashed primary para agregar líneas. |
| Topbar global (search contextual debounce 200ms, Quick Create popover, ayuda contextual) | shell | Search reconoce ruta y filtra OCs; Quick Create incluye "Nueva OC". |
| Sub-topbar del detalle sticky con `data-print="hidden"` | shell | Para impresión limpia del PDF interno. |

### 14.5 Frontend — extender / parametrizar

| Pieza existente | Cambio | Razón |
|---|---|---|
| `<EstadoBadge>` | Agregar 7 estados de OC (Borrador, EnAutorizacionJefeCompras, EnAutorizacionDireccion, Autorizada, Cerrada, Cancelada, Rechazada) con colores y tooltips de glosario. Verificar contraste WCAG AA. | El componente está parametrizado por enum; agregar estados sin reescribir. |
| `nav.ts` (sidebar) | Agregar item "Órdenes de compra" bajo Compras, gated por `compras.ordenes.leer`. | Mismo patrón que RQ. |
| `glosario.ts` | Agregar términos OC (sub-estados, consolidación, duplicar, contenedor, ruta, semana, partida abierta). | Tooltips activos en `<DomainTermTooltip>`. |
| `permission-codes.ts` | Agregar 10 constantes `compras.ordenes.*`. | Mismo patrón. |
| `bandeja-search-schema.ts` (patrón) | Crear `bandeja-oc-search-schema.ts` con campos propios (estado, sub-estados, contenedor, ruta, semana, importe). | Mismo patrón Zod-validado. |
| MSW handlers para tests | Extender con endpoints de OC. | Mismo runtime. |

### 14.6 Frontend — nuevo (oportunidades de componentes cross-módulo)

| Pieza nueva | Promover a compartido si... |
|---|---|
| `<SubEstadosBar>` (recepción / facturación / pago) | ...CxP o Recepción la consumen. Probable: vivir en `components/erp/oc-flow/` desde el inicio. |
| `<SelectorRequisicionesConsolidacion>` (multi-select con filtros) | ...otros módulos requieren selectores con multi-select y filtros server-side. Hoy específico de OC. |
| `<InformacionLogisticaForm>` | ...si Recepción o CxP lo necesitan (probable: guía de transporte aparece en recepción). |
| `<InformacionImportacionForm>` | Específico de importaciones; probable que Recepción lo extienda para capturar pedimento. |
| `<AdjuntosManager>` (lista + upload + remove con tipos parametrizados) | **Promover desde el inicio a `components/erp/adjuntos/`**: CxP, Activos Fijos, Recepción y RQ v1.1 lo van a consumir. Diseño guiado por ADR 0024 (blob storage). |
| `<ArbolDocumentos>` (RQ → OC → Recepción → Factura → Pago) | **Promover desde el inicio a `components/erp/trazabilidad/`**: cross-módulo por definición. Punto único de entrada para "ver el ciclo completo del documento" desde cualquier nodo. |
| `<TimelineAutorizaciones>` específico de OC | Reusa el componente de RQ con prop `entidad="orden-compra"`. Antes de duplicar, parametrizar el de RQ. |
| Vistas: Bandeja general, Detalle, Nueva (Sheet), Selector consolidación, Partidas abiertas, Reporte últimos precios, PDF preview | Páginas específicas de OC; siguen la P1–P4 nomenclatura del patrón. |

### 14.7 Reglas de promoción cross-módulo

Para evitar duplicar al construir CxP/Recepción/Activos después de OC:

- **Si tres módulos lo consumen**, vive en `components/erp/` (frontend) o
  `SharedKernel`/`Compras.Domain.Compartido` (backend).
- **Si dos módulos lo consumen**, vive en `components/erp/<domain>/`
  con prop de parametrización.
- **Si solo OC lo consume hoy**, vive en `features/compras/ordenes-compra/`
  con TODO de migración cuando aparezca el segundo consumidor.
- Promoción se hace cuando aparece el segundo consumidor real, no
  preventivamente.

---

## 15. Glosario

| Término | Definición |
|---|---|
| **Orden de Compra (OC)** | Documento que formaliza un compromiso de compra con un proveedor. |
| **Cabecera** | Datos generales de la OC: proveedor, sucursal, fechas, totales, estado. |
| **Línea** | Renglón individual de la OC, asociado a un artículo, cantidad y precio. |
| **Requisición (RQ)** | Documento de solicitud previa que origina una OC (submódulo de Requisiciones). |
| **Consolidación** | Práctica de agrupar varias RQs en una sola OC (flujo §4.2). |
| **Compromiso** | Estado de una RQ que está vinculada a una OC activa, removida del pool de disponibles. |
| **Sub-estado** | Dimensión independiente de progreso de una OC autorizada (recepción, facturación, pago). |
| **Partidas abiertas** | Vista consolidada de OCs activas con sus sub-estados; reporte crítico del mapa funcional §8.2. |
| **Duplicar OC** | Acción que pre-llena una OC nueva (Borrador) desde una OC cancelada o rechazada. Sustituye la "reapertura" tras la decisión C4 cerrada como "cancelar + recrear". Setea `OcOrigenId` para trazabilidad. |
| **Cotización** | Documento del proveedor que sustenta el precio de una OC; adjunto, no entidad. |
| **Información logística** | Conjunto de campos estructurados que reemplaza el texto libre embebido del SAP legacy (transportista, guía, contenedor, ruta, semana, pedimento). |
| **Sub-estado materializado** | Columna en `ordenes_compra` que persiste el sub-estado calculado, actualizada por listeners (C7). |

---

## Revisiones

| Rev. | Fecha | Cambio |
|---|---|---|
| 0.1 | 2026-05-11 | Borrador inicial del diseño v1, construido sobre el mapa funcional 00 y las decisiones cerradas de §10 (sucursal única, cliente final diferido, migración SAP pendiente, CFDI y offline silenciados). |
| 0.2 | 2026-05-11 | Agregada §14 "Reutilización de código" consolidando reuso backend (heredado / extender / nuevo) y frontend (shell común heredado / extender / nuevo) más reglas de promoción cross-módulo. Glosario renumerado §15. |
| 0.3 | 2026-05-11 | Migración inicial SAP diferida post-MVP (corrección de scope): §1.2 "Fuera v1" actualizado; §11 reescrita como "Estrategia de catálogos en MVP" (seeds versionados + GET read-only); §12 fila de DatosMaestros marcada explícitamente como post-MVP; §13.2 actualizada. Catálogos en MVP son read-only sin CRUD. |
| 0.4 | 2026-05-11 | Cierre del backend tras 3 rondas de decisiones con owner. **C4 cerrado como "cancelar + recrear"** (descartada reapertura): eliminada entidad `VersionOC`, estado `EnReautorizacion`, comandos `ReabrirParaEdicion` / `RegistrarVersionPostReautorizacion`, tabla `compras.orden_compra_versiones`, evento `OrdenCompraReabiertaEvent`, permiso `compras.ordenes.reabrir`. **Agregado** comando `DuplicarOrdenCompraCommand` + campo `OcOrigenId` (con índice) + evento `OrdenCompraDuplicadaEvent` + query `ObtenerOrdenCompraOrigenQuery`. **Confirmadas** C1 (MXN+T/C manual), C2 (folio `OC-MID2026-000001`), C3 (motor impuestos local), C6 (7 tipos adjunto), C7 (sub-estados materializados), C9 (RQ comprometida bloqueada), C10 (validez proveedor en cada transición), C11 (cotización en agregado). **Decididos**: librería PDF = QuestPDF, idempotency-key desde el inicio de cada PR. |
| 0.5 | 2026-05-11 | Cierre del frontend tras 3 rondas de decisiones con owner. **FOC11** agrega 10º permiso canónico `compras.ordenes.crear.sin_rq` para crear OC sin requisición previa (caso especial §4.3 del mapa funcional). **Agregada query** `ObtenerHistoricoOrdenCompraQuery` para alimentar el tab "Historial" del frontend P3 (brecha §14.1 del 05). FOC16 cierra P2 sin acciones inline; el resto de FOC1–FOC15 confirma defaults recomendados. §14 brechas confirmadas con endpoints dedicados (`/historico`, `/duplicadas`, `/partidas-abiertas/kpis`). |
| 0.6 | 2026-05-13 | **Setting `AutoGenerarOcAlAutorizar` por empresa** (ADR-0033). §3.bis.1 actualizada: el filtro del selector pasa de `estado='Autorizada'` a `estado='EnSurtido' AND comprometida_en_oc_id IS NULL` (unificado para los dos modos del setting). Los handlers `CrearOrdenCompraDesdeRequisicion` (1:1) y `AgregarLineaDesdeRequisicion` (N:1) heredan `cantidad_de_compra` (no `cantidad` original), porque la parte cubierta por almacén ya tiene movimiento de salida al autorizar. Modelo y semánticas de §1.2 (1:1 / N:1) sin cambios. |
