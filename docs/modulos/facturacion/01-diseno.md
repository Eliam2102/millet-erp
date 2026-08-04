# Diseño — Módulo Facturación (`Millet.Facturacion`)

> **Proyecto:** ERP Millet — Módulo 3 del back-office (Facturación CFDI 4.0).
> **Versión:** 1.0 — Diseño inicial.
> **Fecha:** 2026-05-29
> **Owner:** Eduardo Paredes — `eduardo.paredes@tiglass.net`
>
> **Documento previo obligatorio:** [`00-levantamiento.md`](00-levantamiento.md).
> Este diseño asume todo el funcional de ese documento (entidades, flujos,
> reglas críticas D1–D17). Aquí se traduce a arquitectura: dominio, esquema
> Postgres, puertos, CQRS, eventos, workers, RBAC, endpoints, frontend.
>
> **Patrón:** exemplar de **Compras** y **Cuentas por Pagar** — hexagonal +
> CQRS con MediatR, Application/Domain/Infrastructure, DbContext propio con
> esquema separado ([ADR-0030](../../decisiones/0030-multi-dbcontext-por-modulo.md)).

---

## 0. Cómo leer este documento

- `[Verificado]` — confirmado con el owner o leído del código actual.
- `[Inferido]` — deducido por convenciones; no confirmado.
- `[Gap]` — requiere confirmación antes de implementar.
- `[Decisión 01]` — decisión tomada en este diseño (no estaba en el
  levantamiento), con su justificación.

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

Facturación es el módulo que produce el lado **emitido** de la operación
fiscal. Se ubica entre:

- **Aguas arriba** (orígenes de venta): `Millet.Integraciones.Aw` (pedidos
  A+W) y `Millet.Integraciones.Origenes` (Planta Pintura + Sistema de
  Salidas). Facturación los consume vía puertos de lectura; **nunca** lee
  sus vistas SQL on-prem directamente.
- **Aguas abajo** (consumo fiscal/financiero): Contabilidad (asientos),
  CxC (saldos por cobrar), Tesorería/Ingresos (cobros), Obras (reportes por
  obra). Facturación publica eventos; no llama síncrono a estos módulos.
- **Lateral** (servicio fiscal): `Millet.Integraciones.Fiscal` — Facturación
  consume el puerto `IFiscalApiClient` (timbrar/cancelar/consultar) y
  `ICsdProvider`. **No** implementa el client del PAC.

```
  Integraciones.Aw ◄─┐ (lectura vistas)                                  ┌─► Contabilidad / CxC
   (bidireccional)   ├─► Facturacion ──(IFiscalApiClient)──► Integraciones.Fiscal ─► FiscalAPI ─► SAT
        ▲            │        │                                          └─► Tesoreria / Obras
        │ claim +    │        ├──(eventos Outbox)──────────────────────────────►
        │ resultado  │        │
        └────────────┘        └──(write-back: claim al ingestar, UUID+estatus 115/70 al facturar)──► A+W
  Integraciones.Origenes ─► Facturacion   (Planta Pintura = pedidos; Salidas = pedimento, no pedidos)
```

### 1.2 Alcance funcional v1 (MVP) — **amplio con complementos** `[Decisión 01]`

Por decisión del owner (2026-05-29), el MVP es **amplio**: cubre los
complementos pesados desde la primera entrega.

**En el MVP:**

| Bloque | Incluye |
|---|---|
| Ingesta automática | `PedidoFacturable` desde **A+W y Planta Pintura** (solo estos dos originan pedidos); validación contra masters; bandeja de excepciones; idempotencia; snapshot |
| **Captura manual** | `PedidoFacturable` con `origen = Manual` capturado en pantalla con líneas inline (estilo Requisiciones); transversal a todos los comportamientos fiscales (§7.10 levantamiento) |
| Pedimento (Salidas) | Asociación del pedimento de exportación leído del Sistema de Salidas (Hoja de Salida ↔ pedimento); **no** origina pedidos. Secuencia respecto al timbre por confirmar (§3.bis.5) |
| Mostrador inmediato | Emisión factura I, multi-forma de pago, NC por bonificación inmediata, REPP si PPD/multipago |
| Maquila / anticipos | Factura de anticipo, Control de Anticipos, vinculación (M2), NC de amortización autogenerada (M3), saldo amortizable |
| Reparto | Emisión con forma 99 + método PPD, liquidación de ruta, REPP con forma real |
| Obras y proyectos | Factura directa + desde pedido, campo Obra (2 columnas); cada estimación es un pedido discreto (no estado parcial, D19) |
| Administrativa | Captura directa, retención por artículo, cuenta contable |
| **Exportación con CCE** | Complemento Comercio Exterior, IVA 0%, pedimento en XML normal, PDF bilingüe, aplicación de pedimento post-timbrado desde Salidas |
| **Carta Porte 3.1** | Entidad propia, tipos T e I, multi-tramo ("siguiente tramo"), mercancías |
| Venta de activos fijos | Validación de alta como activo, autorización del Contador General, asientos de baja |
| Cancelación | Flujo SAT 4.0 con motivos, UUID sustituto, aceptación del receptor, poller asíncrono |
| Complementos de pago | REPP (Pago 2.0), multi-factura, parcialidades |
| Reportes | Liquidación de caja, Control de Anticipos, Estados de facturas de anticipo |

**Fuera del MVP (Fase 2)** — modelado pero no implementado:

- Factura Global periódica (R1) — modo `Pendiente de ticket` reservado.
- NC por devolución de mercancía (requiere Inventario).
- Cuenta puente "Obras Entregadas No Facturadas".
- Reporte unificado de Obras (vive en Obras).
- Complemento Nómina (probablemente operado desde RH).
- `Ticket de mostrador` (facturación online, §4.9 del levantamiento).

> **Riesgo de scope amplio:** Exportación/CCE y Carta Porte dependen de la
> **fase 2 de `Integraciones.Fiscal`** (timbrado/cancelación), que está en
> rediseño asíncrono y bloquea PRs nuevos hasta cerrar D1–D10 + sandbox
> (ver [`integraciones-fiscal/02-flujo-asincrono.md`](../integraciones-fiscal/02-flujo-asincrono.md)).
> El dominio y el esquema de Facturación se pueden construir contra un
> **stub `IFiscalApiClient`**; el cierre real de timbrado/cancelación con
> complementos espera a esa fase 2. Ver §3 y §14.

### 1.3 Volúmenes esperados `[Inferido]`

- Facturas/día: cientos (mostrador + reparto + obras de varias sucursales).
  Pico en cierres de mes.
- Anticipos abiertos simultáneos: miles (obras de larga duración).
- Carta Portes: decenas/día (reparto fuera de municipio + Conkal).
- CFDIs históricos a importar (descarga emitidos): potencialmente decenas de
  miles → fecha de corte y paginación.

Implicaciones: índices por `uuid`, `pedido_origen`, `cliente_id`, `estado`,
`periodo_contable`; particionar tablas de comprobantes por año si crece.

---

## 2. Decisiones de diseño y ADRs aplicados

| Tema | Decisión | ADR / Fuente |
|---|---|---|
| Estructura interna | Hexagonal + CQRS con MediatR; Application/Domain/Infrastructure | Exemplar Compras |
| Persistencia | `FacturacionDbContext`, esquema `facturacion`, dueño único | [ADR-0030](../../decisiones/0030-multi-dbcontext-por-modulo.md) |
| Eventos de integración | Outbox transaccional + Service Bus; naming `{Agregado}{Verbo}Event` | [ADR-0009](../../decisiones/0009-outbox-pattern-eventos-integracion.md) |
| Idempotencia HTTP | `Idempotency-Key` en mutaciones | [ADR-0020](../../decisiones/0020-idempotencia-http.md) |
| Versionado | `/api/v1/facturacion/...` | [ADR-0021](../../decisiones/0021-versionado-api-rest.md) |
| RBAC | Permisos canónicos `facturacion.*` en `Identidad.Domain.PermisosCanonicos` | [ADR-0007](../../decisiones/0007-autorizacion-rbac-granular.md) |
| Errores | Problem Details RFC 7807 vía `IExceptionHandler` | [ADR-0010](../../decisiones/0010-manejo-errores-problem-details.md) |
| Concurrencia | ETag/If-Match en recursos editables (borradores) | [ADR-0012](../../decisiones/0012-concurrencia-hibrida.md) |
| Deuda de plataforma | `PLATFORM-TODO(<id>)` + tabla §13 | [ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md) |
| Reportería | Motor nativo React + JSON + export client-side; `<ReporteShell>` | [ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md) |
| PAC | FiscalAPI único vía `IFiscalApiClient` de `Integraciones.Fiscal` | [ADR-0038](../../decisiones/0038-fiscalapi-pac-unico.md) |
| Workers | `IHostedService` dentro de `Millet.Api` (no Functions/Container Jobs) | D17 levantamiento |

**Decisiones nuevas de este diseño** (también en §15 del levantamiento como
contexto):

- `[Decisión 01-A]` **Ingesta partida en dos módulos de integración.** A+W
  permanece en `Millet.Integraciones.Aw` (nunca será absorbido por el ERP).
  Planta Pintura + Sistema de Salidas van a un **módulo nuevo**
  `Millet.Integraciones.Origenes` (serán absorbidos eventualmente; el módulo
  es un puente temporal). Facturación consume puertos de ambos; no toca sus
  vistas SQL. *(Nombre del módulo nuevo confirmable — ver §3.)*
- `[Decisión 01-B]` **Repositorio de CFDI común desde ahora.** El archivo
  crudo del CFDI (XML, UUID, sello SAT, PDF estándar, estado SAT) vive en un
  repositorio compartido en `Integraciones.Fiscal` (`cfdi_archivo`),
  consumido vía `ICfdiRepositorioPort`. El `Comprobante` de Facturación y el
  `CfdiRecibido` de CxP **referencian** ese archivo en vez de duplicar el
  blob. Requiere coordinación/migración con CxP (PLATFORM-TODO, §13).
- `[Decisión 01-E]` **Integración A+W bidireccional por cola de solicitudes
  (esquema del ERP).** A+W escribe tres operaciones (Alta/Modificación/
  Cancelación) en una **tabla-puente cuyo esquema define el ERP** (D18); el
  ERP la consume en orden por `version` y escribe de vuelta claim
  (`erp_pedido_id`, **persiste siempre**), `estado_facturacion` (binario) y
  UUID. **Doble candado:** la cola (entrada) + `ingesta_control` (fuente de
  verdad). El claim no se borra para señalizar → cero huérfanos.
- `[Decisión 01-F]` **Sin estado parcial; la factura manda** (D19, D20). El
  `PedidoFacturable` es binario `SinFacturar`/`Facturado`; las parciales son
  pedidos discretos. Modificación/Cancelación de A+W solo se auto-aplican sin
  CFDI; con CFDI → revisión manual (el ERP no cancela CFDIs en automático).
- `[Decisión 01-C]` **Timbrado asíncrono-tolerante.** El `Comprobante`
  modela el ciclo timbrado como una FSM con estados intermedios
  (`Borrador → TimbradoEnProceso → Timbrado | TimbradoFallido`) y la
  cancelación con `CancelacionPendiente`. No se asume respuesta síncrona del
  PAC. Esto desacopla a Facturación del rediseño asíncrono de
  `Integraciones.Fiscal`.
- `[Decisión 01-G]` **Reintento de timbrado sobre el mismo comprobante**
  (2026-07-12, owner). Ver §2.bis — sub-decisiones G1–G7, consecuencias y
  plan de implementación en la Fase 13 del
  [03-pr-breakdown.md](03-pr-breakdown.md).

### 2.bis `[Decisión 01-G]` — Reintento de timbrado sobre el mismo comprobante

**Contexto y problema.** Esta FSM (§4.4) y el runbook
([08-operacion-y-runbook.md](08-operacion-y-runbook.md), tabla de errores)
describían desde el origen `TimbradoFallido (corregible → Borrador)` — es
decir, el **mismo** comprobante vuelve a Borrador y se reintenta. Esa
transición no se implementó en F1–F12: la implementación original dejaba la
factura fallida como registro muerto (el pedido seguía `Importado`) y el
reintento creaba una `FacturaVenta` **nueva con folio nuevo** vía
`ReservarFolioCommand`. El PR #530 implementó el reintento (G1/G2/G6/G7); la
Fase 13 completa el modelo con G3/G4/G5. Consecuencias observadas en pruebas
(2026-07-11):

- **Consumo de consecutivos por cada rechazo del PAC** (errores corregibles
  tipo `CFDI40xxx` queman un folio por intento). El "hueco aceptable" del
  diseño original aplicaba solo al fallo raro de persistencia post-reserva,
  no a cada rechazo.
- **Bandeja de facturas ilegible**: los intentos aparecen como facturas
  hermanas (F-101 fallida, F-102 fallida, F-103 timbrada) en lugar de un
  documento con historial.
- **Reconciliación `PAC_TIMEOUT` frágil**: el runbook busca en FiscalAPI por
  serie+folio; con folio distinto por intento la búsqueda no es determinista.
- Gap de consistencia: si el timbre queda ambiguo (`TimbradoEnProceso`), el
  pedido sí se marca `Facturado`; cuando `TimbradoPendienteWorker` lo vence a
  `PAC_TIMEOUT`, nadie revierte el pedido → pedido atascado apuntando a una
  fallida.

**Referencia externa.** El patrón es uniforme en los ERP con CFDI (SAP
eDocument Framework, Odoo `l10n_mx_edi`, Dynamics 365 BC MX, CONTPAQi,
Aspel): el documento y su folio nacen **una sola vez**; el timbrado es un
sub-estado reintentable sobre ese documento, con los intentos como bitácora.
Un documento nuevo (folio nuevo) solo nace por cancelación + sustitución
(relación 04), nunca por un reintento técnico.

**Sub-decisiones:**

- **G1 — El folio se asigna una vez por documento.** `ReservarFolioCommand`
  se invoca solo al crear el comprobante; el reintento reutiliza serie+folio.
  Huecos en consecutivos solo por `Descartada` (G3) o por el fallo raro de
  persistencia post-reserva (ya aceptado).
- **G2 — Transición FSM `TimbradoFallido → Borrador`**
  (`Comprobante.ReabrirParaReintentoTimbrado`, #530). Vive en el
  `Comprobante` base y el endpoint genérico
  `POST /comprobantes/{id}/reintentar-timbrado` (permiso
  `facturacion.comprobantes.reintentar-timbrado`) cubre los cinco tipos:
  factura de venta, factura de anticipo, NC, REPP y carta porte —
  `ReintentarTimbradoCommand` despacha por tipo y replica los efectos
  post-éxito de cada handler de emisión.
- **G3 — Estado terminal `Descartada`** (`EstadoTimbrado = 8`; el enum es
  append-only por ABI). Solo alcanzable desde `TimbradoFallido`. Para
  fallidas que no se reintentarán (pedido cancelado en origen, captura
  errónea de raíz). Quema el folio de forma consciente y auditada, y libera
  el pedido (`RevertirAFacturable`).
- **G4 — Bitácora de intentos** `bitacora_intento_timbrado`: una fila por
  llamada al PAC (`comprobante_id`, `intento_numero`, resultado,
  `error_codigo`, `error_mensaje`, `registrado_at`), escrita por
  `TimbradoEjecutor` en la misma transacción que la FSM. Los campos
  `TimbradoErrorCodigo/Mensaje` del comprobante quedan como caché del último
  intento. Consulta: `GET /comprobantes/{id}/intentos-timbrado`. Patrón:
  `BitacoraEnvioCorreo`.
- **G5 — El pedido queda tomado por su factura desde el primer intento.**
  Modelo mental: **1 pedido → 1 documento → N intentos**. `EmitirFacturaVenta`
  marca el pedido `Facturado` siempre que la factura persiste (se elimina la
  excepción para `TimbradoFallido`). Se redefine la semántica: `Facturado` =
  "tiene documento de factura **vivo** (no cancelado, no descartado)", ya no
  "tiene CFDI vigente". El write-back a la tabla-puente A+W sigue
  condicionado a `Timbrado` (sin cambio). Esto elimina por construcción las
  facturas hermanas y el gap del pedido atascado tras `PAC_TIMEOUT`.
- **G6 — Corrección de datos en el reintento** (#530). La `CfdiEmision` se
  reconstruye desde los **datos actuales** del agregado, sus catálogos y la
  configuración vigente (CSD, identidades sandbox), con fecha CFDI nueva —
  una corrección entre intentos viaja en el reintento. Los **snapshots** de
  receptor/emisor del comprobante NO se re-resuelven del master: si el
  snapshot mismo quedó mal capturado, la salida es **Descartar (G3) + emitir
  de nuevo** (la nueva emisión toma el master corregido).
- **G7 — Compuerta de fallos ambiguos** (#530). Reintentar una fallida con
  código `PAC_TIMEOUT` / `PAC_SIN_RESPUESTA` / `PAC_RESPUESTA_INCOMPLETA`
  exige `confirmarNoDuplicado=true` (si falta: `REINTENTO_REQUIERE_CONFIRMACION`)
  tras verificar en FiscalAPI que **no** existe un timbre de este comprobante
  (runbook §5.bis) — re-timbrar a ciegas duplicaría el CFDI ante el SAT. El
  folio retenido (G1) hace esa búsqueda determinista.

**Consecuencias.**

**Positivas** — el invariante "1 pedido → ≤1 documento vivo" se cumple por
construcción; desaparece el gap del pedido atascado; la bandeja muestra un
documento por factura con su historial; la reconciliación por serie+folio es
determinista; los folios dejan de consumirse por errores corregibles.

**Negativas / deuda** — `Descartada` es un valor nuevo de enum persistido:
espejo del enum en el frontend en el mismo PR (incidente `FacturaAnticipo`
2026-07-11, #526/#528; `comprobante.estado` no tiene check constraint —
verificado). Requiere migración de datos históricos (fallidas huérfanas →
`Descartada`; pedidos `Importado` con una fallida viva → backfill G5). El
endpoint de descarte introduce el permiso canónico
`facturacion.comprobantes.descartar` (migration en `IdentidadDbContext`).
Con G5, una Modificación/Cancelación de A+W sobre un pedido cuya factura
está fallida cae a revisión manual (el pedido está `Facturado`); el operador
descarta la fallida y la solicitud se reprocesa.

**Plan de implementación:** Fase 13 en [03-pr-breakdown.md](03-pr-breakdown.md).

---

## 3. Asunciones que deben confirmarse

| # | Asunción | Impacto si es falsa |
|---|---|---|
| A1 | El nombre del módulo nuevo de orígenes es `Millet.Integraciones.Origenes`, esquema `integraciones_origenes`. | Renombrado mecánico antes de la primera migración. |
| A2 | A+W expone (o expondrá) una vista de "pedidos listos para facturar" consumible por `Integraciones.Aw`; hoy ese módulo es push del Glass Agent. | Si A+W no publica vista, el reader de Aw es un stub hasta tener la vista. |
| A3 | El ERP es **master único** de Cliente y Producto (`DatosMaestros`). Los clientes **nacen desde A+W** por auto-provisión al ingestar; la vista de clientes de A+W trae los datos fiscales (RFC, régimen, CP, defaults). | Si la vista no trae datos fiscales completos, el cliente se crea pero no se puede timbrar hasta completarlos. |
| A4 | El master de Producto trae clave SAT, clave unidad, cuenta contable, retención y atributo `origen`. Los `origen = A+W` se auto-provisionan; Planta Pintura exige preexistencia. | Sin clave SAT no se factura (§16.12). |
| A5 | `Integraciones.Fiscal` fase 2 expondrá `IFiscalApiClient` con timbrado de complementos (CCE, Carta Porte, Pago 2.0). | El MVP amplio (CCE/Carta Porte) se difiere hasta que exista. |
| A6 | El candado de período cerrado se consulta vía `IPeriodoContablePort` (Contabilidad). Hasta que exista, stub "siempre abierto". | Sin candado real, riesgo de emitir en período cerrado (mitigado por validación manual). |
| A7 | El repositorio común de CFDI puede vivir en `integraciones_fiscal` sin romper el `CfdiRecibido` actual de CxP (migración coordinada). | Si no, Facturación arranca con repo propio y se unifica después. |
| A8 | Series CFDI vía **`Compartido.Series`** (ya existe `[Verificado]`), configurables; seed inicial con valores conocidos (anticipos = `FANT`). No se crea catálogo propio. | Solo cambia el seed, no el código. |

---

## 3.bis Conceptos derivados de las decisiones

### 3.bis.1 `PedidoFacturable` ≠ pedido del origen; cuatro orígenes

El `PedidoFacturable` es la representación **normalizada y validada** dentro
de `facturacion` de un pedido listo para facturar. Tiene un campo `origen`
con **tres** valores: `Aw`, `PlantaPintura` y **`Manual`**.

- **Orígenes externos** (`Aw`/`PlantaPintura`): el pedido se crea por
  **ingesta** (worker) leyendo vistas vía puertos de integración. Se
  acompaña de un `PedidoFacturableSnapshot` (copia cruda inmutable, para
  auditoría) y puede **refrescarse** si el origen cambia y el pedido aún no
  está facturado. Los errores de validación van a la **bandeja de
  excepciones**.
- **Origen `Manual`**: el operador lo **captura en pantalla** con sus líneas
  (estilo Requisiciones, líneas inline). **No** tiene snapshot de origen, no
  pasa por vistas/Hybrid Connection, no genera bandeja de excepciones (los
  errores se muestran en el formulario) y no tiene `write-back`. Se valida
  contra los mismos masters (Cliente fiscal, Producto con clave SAT).

> **El Sistema de Salidas NO es un origen de `PedidoFacturable`.** Es una
> integración entrante distinta cuyo rol hacia Facturación es proveer el
> **pedimento de exportación** (Hoja de Salida con Contenedor ↔ pedimento) y
> el packing list. Se modela como `ISalidasPedimentosReader` (§6.1) y se
> asocia a las facturas de exportación vía `PedimentoSalidasWorker` (§9), no
> como un pedido. Ver §3.bis.5 para la secuencia respecto al timbre.

A partir del `PedidoFacturable`, **el flujo de emisión es idéntico** sin
importar el origen. La captura manual es transversal a todos los
comportamientos fiscales, no exclusiva de Obras/Administrativa.

### 3.bis.2 `Comprobante` (base) ↔ especializaciones

Todo CFDI comparte la base `Comprobante` (serie/folio, receptor snapshot,
totales, FSM de timbrado, datos de timbrado, relación al `cfdi_archivo`). Las
especializaciones (`FacturaVenta`, `FacturaAnticipo`, `NotaCredito`,
`CartaPorte`, `ReciboPago`) agregan sus campos. EF Core con **table-per-type
(TPT)**: tabla `comprobante` + tabla por subtipo.

### 3.bis.3 `Anticipo` separado del CFDI de anticipo

El CFDI `FacturaAnticipo` es inmutable una vez timbrado. El **saldo
amortizable** vive en un agregado aparte `Anticipo` (estado `Abierto`/
`Amortizado`/`Cancelado`, `monto_cobrado`, `monto_amortizado`,
vinculaciones). Esto permite que el saldo evolucione (cobros PPD, NCs) sin
tocar el CFDI.

### 3.bis.4 NC de amortización atómica con el timbre

`EmitirFacturaVentaCommand` con anticipos vinculados ejecuta, en **una sola
transacción**: timbrar la factura final → crear y timbrar la NC de
amortización → reducir el saldo del/los anticipo(s). Si cualquier paso falla,
rollback completo. (FSM asíncrona-tolerante: si el timbre del PAC queda en
proceso, ambos quedan `TimbradoEnProceso` y se resuelven juntos.)

### 3.bis.5 Pedimento: compuerta condicional, nunca global

**Solo un subconjunto de facturas requiere pedimento.** El pedimento (número
y fecha del documento aduanero) aplica únicamente a líneas de **producto de
importación en su primera venta** (art. 29-A CFF) — sea venta nacional o de
exportación. La inmensa mayoría de facturas (nacionales de producto no
importado, servicios, Planta Pintura, Administrativa) **no llevan pedimento
y se timbran sin esperar nada**.

**`requiere_pedimento` NO es un atributo del catálogo de Producto.** El mismo
artículo puede venderse con o sin pedimento (depende de si ese inventario en
particular fue importado y es su primera venta, no del producto en
abstracto). Por eso el indicador **lo aporta el `PedidoFacturable`**, a nivel
de **línea** (autoritativo) o de **cabecera** (default que aplica a todas las
líneas):

- **Pedido ingestado** (A+W): el indicador viene como columna de la vista
  SQL (línea o cabecera). `[Gap]` contrato de columnas (§15.1).
- **Pedido manual**: el operador lo marca en la captura (checkbox por línea o
  por cabecera).

Una factura "requiere pedimento" si **alguna** de sus líneas lo marca.

**Regla de timbrado `[Gap secuencia]`** — se evalúa por factura, no global:

- `requiere_pedimento = false` → **timbra de inmediato** por el flujo normal.
  Sin compuerta, sin espera. (Caso mayoritario.)
- `requiere_pedimento = true` y pedimento **ya disponible** (capturado o
  leído de Salidas) → timbra con el pedimento en el XML.
- `requiere_pedimento = true` y pedimento **aún no disponible** → la factura
  queda en estado intermedio (borrador retenido / `PendientePedimento`) y
  **no se timbra** hasta que `AplicarPedimentoCommand` la complete. El
  `PedimentoSalidasWorker` empareja la Hoja de Salida ↔ pedimento y dispara
  el timbre. Esta retención afecta **solo** a esa factura, no al resto.

> **Por qué no es post-timbrado en sentido estricto:** el pedimento va en el
> **XML normal** (no en CCE) y un CFDI timbrado es **inmutable**. Si se
> timbrara sin pedimento, incorporarlo después exigiría **cancelar +
> sustituir** (relación 04), consumiendo timbres. El diseño prefiere
> **retener el timbre** de las pocas facturas que sí lo requieren, y soporta
> la sustitución solo como fallback. Confirmar con el sample fiscal y con la
> operación de Millet (§15.1) cuál camino usan hoy. `AplicarPedimentoCommand`
> soporta ambos: completar el borrador retenido **o** disparar sustitución.

### 3.bis.6 Master de Cliente/Producto: ERP master, nacidos desde A+W

El **ERP es master único** de Cliente y Producto (`DatosMaestros`).
Facturación **no** escribe ese master directamente; lo resuelve por puertos
de lectura y, cuando falta, dispara una **auto-provisión** controlada que
depende del origen del pedido:

| Origen del pedido | Cliente/Artículo no existe en el ERP |
|---|---|
| **A+W** | **Auto-provisión:** `DatosMaestros` crea el registro leyendo la vista de master de A+W (vía `IAwClientesReader` / `IAwArticulosReader`). Único caso de alta automática desde vista. |
| **Planta Pintura** | **Excepción** (`cliente_no_existe` / `articulo_no_existe`). Deben crearse antes en el ERP; no se auto-crean. |
| **Manual** | El operador selecciona del master; si no existe, lo da de alta en Administración primero (no auto-provisión en ingesta). |

Flujo de la ingesta A+W (orquestado por Facturación, ejecutado por el dueño
del master):

1. Por cada `cliente_ref` / `producto_ref` del pedido, Facturación llama
   `IClientesReadPort.Resolver(ref)` / `IProductosReadPort.Resolver(ref)`.
2. Si **no existe** y el origen es A+W → llama
   `IMasterProvisioningPort.EnsureClienteDesdeAw(ref)` /
   `EnsureArticuloDesdeAw(ref)`. `DatosMaestros` (no Facturación) crea el
   registro tirando de los readers de `Integraciones.Aw` y devuelve el id.
3. Si no existe y el origen es Planta Pintura → excepción a la bandeja.
4. El artículo se crea con `origen = A+W`.

> **Datos fiscales incompletos ≠ no existe.** La auto-provisión crea el
> cliente aunque le falten datos fiscales; eso **no** bloquea la importación,
> pero sí el **timbrado** (validación al emitir). Así un pedido entra y la
> factura espera a que se complete el RFC/régimen del cliente.

> **Frontera `[Decisión 01-D]`:** el alta del master vive en `DatosMaestros`,
> no en Facturación (cero escritura directa al master de otro módulo). El
> puerto `IMasterProvisioningPort` lo expone `DatosMaestros`, que internamente
> consume los readers de `Integraciones.Aw`. ✅ Implementado (ADR-0048, PR4
> #450): `AwMasterProvisioningAdapter` + commands `Provisionar*` (§13); el
> stub `NoOpMasterProvisioningPort` queda como fallback dev sin Hybrid
> Connection.

### 3.bis.7 Ciclo de vida del `PedidoFacturable` y gestión de operaciones A+W

**Estados (binario respecto a facturación — sin "parcial", §8):**

```
Importado ──(cajero abre a facturar)──► Bloqueado(soft-lock) ──(timbra)──► Facturado
   ▲   ▲ │                                   │
   │   │ └──(Modificación A+W)──► refresh + nuevo snapshot, version++  (solo Importado, sin lock)
   │   │ └──(Cancelación A+W)──► Cancelado                             (solo Importado/Excepcion, sin lock)
   │   └──(se CANCELA el CFDI vigente)──────── Facturado               (vuelve a Importado → re-facturable)
   └──(lock expira sin facturar)─┘
Facturado ──(Modif/Cancel A+W con CFDI vigente)──► NO auto → alerta a revisión manual
```

`PedidoFacturable` y `FacturaVenta` son **entidades separadas**: `Facturado`
es un estado del pedido (= "tiene un CFDI vigente"), no la factura. Un pedido
tiene **≤1 factura vigente** y acumula **N** comprobantes en el tiempo
(cancelados + vigente). Al cancelar el CFDI vigente, el pedido vuelve a
`Importado` (re-facturable); la nueva factura se liga a la cancelada vía
`RelacionCfdi` tipo 04 (sustitución). No hay estado `ParcialmenteFacturado`
(D19): cada `PedidoFacturable` se factura completo; las "parciales" son
pedidos discretos distintos.

**Tres capas de control de actualización** (quién actualiza × cuándo):

| Fuente | Cuándo se permite | Cómo se controla |
|---|---|---|
| Operación A+W (cola de solicitudes) | Solo si `Importado` y sin soft-lock; según matriz §12.1 | `version` por pedido (orden+idempotencia) + `ingesta_control` |
| Edición manual (`origen = Manual`) | Solo `Importado`, sin lock | **ETag/If-Match** ([ADR-0012](../../decisiones/0012-concurrencia-hibrida.md)) |
| Facturación (cajero emite) | Toma el pedido | **Soft-lock** (reusa `SoftLockExpirationWorker` existente) |

**Regla de oro:** una sola escritura "gana" a la vez (soft-lock durante
emisión, ETag durante edición). `Importado` es la única ventana de
actualización libre; tras facturar, el pedido es inmutable desde el origen
(igual que el CFDI) → cualquier operación de A+W va a revisión manual.

**Cola de solicitudes A+W (`aw_solicitud_pedido`)** — esquema **definido por
el ERP** (D18); A+W la llena, el ERP la consume en orden y escribe de vuelta
el claim, el `estado_facturacion` (binario) y el resultado. Matriz operación
× estado en §12.1 (levantamiento) y §5 (esquema).

> **Sin huérfanos:** el claim `erp_pedido_id` **nunca se borra**; las
> operaciones son explícitas, no "ausencia de claim". `ingesta_control` es
> la fuente de verdad aunque A+W reescriba algo (doble candado).

---

## 4. Modelo del dominio

### 4.1 Agregados raíz

| Agregado | Responsabilidad | Notas |
|---|---|---|
| `Comprobante` (base abstracta) | Ciclo de timbrado, datos fiscales comunes, relaciones CFDI | TPT; raíz de los 5 subtipos |
| `FacturaVenta` | Venta de bienes/servicios (I), líneas, CCE, Obra, datos aduaneros, anticipos aplicados | Canal × comportamiento fiscal |
| `FacturaAnticipo` | CFDI de anticipo (I, serie FANT) | Siempre nominal |
| `NotaCredito` | NC (E): amortización / bonificación / devolución | Relación 07/01/03 |
| `CartaPorte` | Traslado (T o I), tramo, vehículo, operador, mercancías | Entidad propia, multi-tramo |
| `ReciboPago` (REPP) | Pago (P) con complemento Pago 2.0 | Multi-factura, parcialidades |
| `Anticipo` | Saldo amortizable, estado, vinculaciones | Separado del CFDI |
| `PedidoFacturable` | Pedido normalizado listo para facturar (+ snapshot si es externo) | `origen` ∈ {Aw, PlantaPintura, **Manual**}; ingesta o captura. Salidas NO es origen de pedido |
| `SolicitudCancelacion` | Workflow de cancelación SAT 4.0 | FSM propia |
| `BitacoraEnvioCorreo` | Envío de CFDIs al cliente + reintentos | Por comprobante |

### 4.2 Value Objects

- `DatosFiscalesReceptor` — snapshot al emitir: RFC, nombre, régimen, CP
  fiscal, uso CFDI, país (para genérico `XAXX`/`XEXX`).
- `DatosTimbrado` — UUID, sello CFDI, sello SAT, fecha timbrado, RFC PAC,
  `cfdi_archivo_id` (ref al repo común).
- `Importe` — `monto decimal(18,6)` + `moneda` + `tipo_cambio`. Helpers de
  redondeo SAT (6 decimales unitarios, 2 totales).
- `SerieFolio` — serie + folio consecutivo (por sucursal-tipo).
- `ConceptoLinea` — producto/servicio, clave SAT, clave unidad, cantidad,
  valor unitario, descuento, objeto impuesto, traslados, retenciones,
  **`requiere_pedimento`** (indicado por el pedido en línea o cabecera —
  **no** se deriva del catálogo de Producto; el mismo artículo puede ir con o
  sin pedimento) y datos aduaneros (pedimento, fecha doc aduanero,
  identificación de mercancía) — estos últimos nullable hasta que se
  apliquen.
- `RelacionCfdi` — UUID relacionado + `c_TipoRelacion` (01/03/04/07).
- `PeriodoContable` — año-mes; usado por el candado.
- `FormaPagoAplicada` — `c_FormaPago` + importe + datos bancarios (4 últimos
  dígitos de tarjeta; PCI-DSS: nunca PAN completo) + fecha emisión/vencimiento
  (cheque).
- `DatosAduana` (línea) — número pedimento, fecha, identificación.

### 4.3 Invariantes principales

1. **Candado de período** — no emitir/cancelar/modificar un `Comprobante`
   con `periodo_contable` cerrado (`IPeriodoContablePort`). [D13]
2. **Saldo amortizable** — `Anticipo.saldo = monto_cobrado − monto_amortizado`;
   no se puede amortizar más que lo cobrado. [D6]
3. **NC de amortización atómica** — se autogenera y timbra en la misma
   transacción que la factura final; relaciona la factura de anticipo Y la
   factura final (relación 07). [D5]
4. **Anticipo siempre nominal** — `FacturaAnticipo` no admite RFC genérico.
5. **A lo más una factura viva por pedido** — un `PedidoFacturable` tiene
   ≤1 `FacturaVenta` **viva** (no cancelada, no descartada) a la vez, desde
   el primer intento de timbrado (no se emiten dos en paralelo) [01-G G5].
   **Tras cancelar el CFDI (o descartar una fallida), el pedido vuelve a
   `Importado` y es re-facturable.** Un pedido acumula **N** comprobantes en
   el tiempo únicamente por cancelación/descarte — nunca por reintento de
   timbrado; el CFDI cancelado nunca se borra. Las parciales son pedidos
   discretos distintos (D19).
6. **NC sólo sobre factura no cancelada** — bloquear NC si origen `Cancelado`.
7. **Bonificación nunca en el XML de la venta** — va como NC posterior. [D7]
8. **Cancelar anticipo amortizado** requiere cancelar primero las NCs de
   amortización relacionadas (validar cadena de `RelacionCfdi`).
9. **Compuerta de pedimento condicional** — una factura con alguna línea
   `requiere_pedimento = true` **no puede timbrarse** hasta que esas líneas
   tengan pedimento (queda en `Borrador`/`PendientePedimento`). Las facturas
   sin líneas que lo requieran timbran sin restricción. La compuerta es por
   factura, **nunca global** (§3.bis.5).
10. **Comentarios del origen jamás al XML** — `PedidoFacturable.comentarios`
   no se mapea a ningún concepto del CFDI.
11. **Carta Porte por tramo** — un cambio de vehículo/operador crea un
    `CartaPorte` nuevo, referenciando el anterior; no se muta el existente.
12. **CFDI timbrado inmutable** — toda corrección es cancelación +
    sustitución (relación 04). [Apéndice levantamiento]
13. **El folio se asigna una vez por documento** — el reintento de timbrado
    reutiliza serie+folio; nunca consume un consecutivo nuevo. Huecos solo
    por `Descartada` o fallo de persistencia post-reserva. [01-G G1]

### 4.4 FSM de timbrado del `Comprobante`

```
Borrador ──(requiere_pedimento sin pedimento)──► PendientePedimento ──(AplicarPedimento)──► Borrador
Borrador ──(emitir)──► TimbradoEnProceso ──(PAC OK)──► Timbrado
                            │
                            └──(PAC error)──► TimbradoFallido
TimbradoFallido ──(reintentar [G7 si PAC_TIMEOUT])──► Borrador  (mismo comprobante, mismo folio [01-G])
TimbradoFallido ──(descartar)──► Descartada  (terminal; quema el folio y libera el pedido)
Timbrado ──(solicitar cancelación)──► CancelacionPendiente ──┬─► Cancelado
                                                             └─► Timbrado (rechazada)
```

Cada llamada al PAC (emisión inicial o reintento) registra una fila en
`bitacora_intento_timbrado` [01-G G4]; el comprobante conserva solo el
código/mensaje del último intento.

`PendientePedimento` solo aplica a las facturas con líneas
`requiere_pedimento = true` y bloquea **únicamente** a esa factura (§3.bis.5);
la mayoría pasa directo `Borrador → TimbradoEnProceso`. `TimbradoEnProceso` y
`CancelacionPendiente` permiten que el flujo no asuma respuesta síncrona del
PAC (Decisión 01-C). Un worker resuelve los pendientes.

### 4.5 FSM de `SolicitudCancelacion`

`Solicitada → EnProceso → {Aceptada | Rechazada | Vencida}` (vencida = sin
respuesta del receptor en 3 días hábiles → aceptación tácita). Re-emisión
automática del sustituto cuando motivo = 01.

---

## 5. Esquema PostgreSQL (`facturacion`)

Tablas principales (no exhaustivo; nombres en `snake_case`):

**Comprobantes (TPT):**
- `comprobante` — id, tipo (I/E/T/P), serie, folio, sucursal_id, caja_id
  (reservado a la sesión de efectivo — Capa B de [`12-cajas.md`](12-cajas.md);
  el alcance de datos NO se deriva de esta columna),
  usuario_emisor_id, receptor (columnas snapshot), regimen_emisor,
  uso_cfdi, metodo_pago, forma_pago, moneda, tipo_cambio, subtotal,
  descuento, impuestos_trasladados, retenciones, total, estado (FSM),
  periodo_contable, cfdi_archivo_id (→ integraciones_fiscal), uuid, sello_cfdi,
  sello_sat, fecha_timbrado, rfc_pac, enviado_correo, row_version (xmin).
- `factura_venta` — comprobante_id (PK/FK), pedido_facturable_id? (**1 pedido
  → N facturas en el tiempo**; el historial completo cuelga del pedido),
  canal_venta,
  comportamiento_fiscal, obra_id, obra_nombre, factura_agrupada,
  autorizacion_id?.
- `factura_venta_linea` — factura_id, producto_id, clave_sat, clave_unidad,
  cantidad, valor_unitario, descuento, objeto_imp, retencion_*,
  requiere_pedimento (bool), pedimento (nullable), fecha_doc_aduanero
  (nullable), identificacion_mercancia (nullable).
- `factura_anticipo` — comprobante_id, tipo_anticipo (MXP/USD), pedido_id?,
  anticipo_id (→ anticipo).
- `nota_credito` — comprobante_id, motivo, anticipo_origen_id?,
  importe_afecta_inventario.
- `carta_porte` — comprobante_id, origen, destino, distancia, vehiculo_id,
  operador_id, carta_porte_previa_id?, tipo_cfdi (T/I), fecha_salida,
  fecha_llegada_estimada.
- `carta_porte_mercancia` — carta_porte_id, descripcion, peso, cantidad.
- `recibo_pago` — comprobante_id, importe_total, fecha_pago.
- `recibo_pago_factura` — recibo_pago_id, factura_uuid, num_parcialidad,
  importe, forma_pago_real, datos_bancarios, tc_pago, ganancia_perdida_cambiaria.

**Relaciones y complementos:**
- `relacion_cfdi` — comprobante_id, uuid_relacionado, tipo_relacion.
- `complemento_cce` — factura_id, tipo_operacion, incoterm, tc_dof, receptor_*.
- `complemento_cce_linea` — cce_id, fraccion_arancelaria, unidad_aduana,
  valor_usd, cantidad_aduana, aplica_iva_0.

**Anticipos:**
- `anticipo` — id, cliente_id, tipo (MXP/USD), monto_cobrado, monto_amortizado,
  saldo (derivado/persistido), estado, factura_anticipo_id, pedido_origen_ref.
- `anticipo_vinculacion` — anticipo_id, factura_venta_id, nc_amortizacion_id,
  importe_amortizado.

**Inbound:**
- `pedido_facturable` — id, **origen (Aw/PlantaPintura/Manual)**,
  numero_pedido (nullable para Manual; folio interno si manual), sucursal,
  cliente_ref, canal_venta, comportamiento_fiscal_sugerido, obra_id,
  obra_nombre, moneda, total, estado_origen (nullable para Manual),
  version_origen (nullable para Manual), estado
  (Importado/Bloqueado/Facturado/Cancelado/Excepcion — **binario respecto a
  facturación, sin Parcial**), comprobante_vigente_id? (la `FacturaVenta`
  **viva** actual — no cancelada ni descartada; se asigna desde el primer
  intento de timbrado [01-G G5]; null si `Importado` — al cancelar el CFDI o
  descartar la fallida vuelve a null y el estado a `Importado`,
  re-facturable), row_version (ETag), soft_lock_por?,
  soft_lock_expira_at?, capturado_por (nullable; set si Manual),
  requiere_pedimento_cabecera (bool default false; aplica a todas las líneas
  salvo override en la línea).
- `pedido_facturable_linea` — pedido_id, producto_ref, cantidad, precio,
  descuento, almacen, requiere_pedimento (nullable bool; override de la
  cabecera — el mismo producto puede ir con o sin pedimento), bom_json?
  (informativo).
- `pedido_facturable_snapshot` — pedido_id, payload_crudo (jsonb), leido_at.
  **Solo para orígenes externos** (los manuales no tienen snapshot).
- `ingesta_control` — origen, clave_natural (numero_pedido del origen),
  hash_contenido, ultima_version_aplicada, estado_ingesta
  (Importado/Excepcion/Facturado/Cancelado/Ignorado), pedido_facturable_id?,
  ultima_lectura_at. **Único `(origen, clave_natural)`.** Memoria de la
  ingesta: idempotencia + detección de cambios — fuente de verdad aunque A+W
  reescriba algo. No aplica a `origen = Manual`.
- `aw_solicitud_pedido` (tabla-puente on-prem, **esquema definido por el
  ERP**, D18) — solicitud_id, numero_pedido, operacion
  (Alta/Modificacion/Cancelacion), version, creada_at | (ERP escribe:)
  erp_pedido_id (claim, persiste), estado_facturacion
  (SinFacturar/Facturado/Cancelado), uuid?, resultado
  (Aplicada/Rechazada/Pospuesta/Error), motivo?, procesada_at. El ERP la
  consume en orden por (numero_pedido, version).
- `bandeja_excepcion_importacion` — pedido_ref, motivo (cliente_no_existe…),
  detalle, resuelto, resuelto_por, resuelto_at.

**Cancelación, envíos, control:**
- `solicitud_cancelacion` — comprobante_id, motivo, uuid_sustituto?, estado,
  bitacora_jsonb.
- `bitacora_envio_correo` — comprobante_id, destinatario, intento, estado,
  enviado_at, error?.
- `bitacora_intento_timbrado` — comprobante_id, intento_n, resultado
  (Timbrado/Fallido/EnProceso), error_codigo?, error_mensaje?, timestamp.
  Una fila por llamada al PAC (emisión inicial o reintento) [01-G G4];
  patrón `BitacoraEnvioCorreo`.
- **Series/folios:** se **reutiliza `Compartido.Series`** (CRUD +
  `ReservarFolioCommand` + migración `SeriesYSecuenciasFolio`, ya en el repo
  `[Verificado]`). No se crea tabla `serie` propia. Series **configurables**;
  **seed inicial** con los valores conocidos (anticipos = `FANT`; resto por
  sucursal-tipo). Facturación reserva folios atómicamente vía ese servicio.
- `concepto_contable_map` — concepto (enum), cuenta_codigo,
  requiere_codigo_definitivo (seed con `TBD-*`). `[Inferido]` puede vivir en
  Administración/Contabilidad.

### 5.1 Índices críticos

- `comprobante(uuid)` único parcial (where uuid not null).
- `comprobante(estado)`, `comprobante(periodo_contable)`,
  `comprobante(sucursal_id, serie, folio)` único.
- `factura_venta(obra_id)`, `factura_venta(pedido_facturable_id)`.
- `anticipo(cliente_id, estado)`.
- `ingesta_control(origen, clave_natural)` único — idempotencia y detección
  de cambios de la ingesta (no aplica a `origen = Manual`).
- `ingesta_control(estado_ingesta)` para barrer pendientes/excepciones.
- `aw_solicitud_pedido(resultado, numero_pedido, version)` — el worker barre
  solicitudes sin procesar en orden (índice sobre la tabla-puente on-prem).
- `relacion_cfdi(uuid_relacionado)`.

### 5.2 Particionamiento

`comprobante` y subtipos particionables por año de `fecha_timbrado` si el
volumen lo exige (§1.3). No en MVP; reservar la estrategia.

### 5.3 Migraciones

DbContext nuevo `FacturacionDbContext`. **Checklist obligatorio** (memoria
[feedback_dbcontext_nuevo_checklist]): registrar en `Program.cs`
(`AddDbContext` + `MigrationsHealthCheckOptions.ContextTypes`) **y** en el
bucle de migraciones de `deploy-app-dev.yml` en el mismo PR; sin esto el
deploy falla con `/health/ready` 503.

---

## 6. Puertos y adaptadores

### 6.1 Puertos de lectura (Facturación consume)

| Puerto | Dueño | Para qué |
|---|---|---|
| `IClientesReadPort` | DatosMaestros | Resolver/leer datos fiscales del cliente (RFC, régimen, CP, defaults, multimoneda) |
| `IProductosReadPort` | DatosMaestros | Resolver clave SAT, clave unidad, objeto imp, tasa, retención, `origen` por artículo |
| `IMasterProvisioningPort` | DatosMaestros | `EnsureClienteDesdeAw(ref)` / `EnsureArticuloDesdeAw(ref)` — auto-provisión del master desde vistas A+W (§3.bis.6). Solo origen A+W |
| `IAwClientesReader` / `IAwArticulosReader` | Integraciones.Aw | Vistas de master de A+W (clientes/artículos con datos fiscales). Consumidos por `DatosMaestros` para la provisión, no por Facturación directamente |
| `IActivosFijosReadPort` | Activos Fijos (o catálogo mínimo) | Valor en libros, depreciación, validación de alta |
| `IAwSolicitudesReader` | Integraciones.Aw | Lee la cola `aw_solicitud_pedido` (operaciones pendientes) y los datos del pedido (cabecera/líneas) que cada solicitud referencia |
| `IPlantaPinturaPedidosReader` | Integraciones.Origenes | **Órdenes de servicio de Planta Pintura** listas para facturar (vista SQL). Único reader de pedidos del módulo Origenes |
| `ISalidasPedimentosReader` | Integraciones.Origenes | Hoja de Salida con Contenedor ↔ pedimento + packing list. **No** provee pedidos; alimenta la asociación de pedimento (§3.bis.5) |
| `IPeriodoContablePort` | Contabilidad | ¿Período cerrado? (candado) |
| `IFiscalApiClient` | Integraciones.Fiscal | Timbrar / cancelar / consultar estatus |
| `ICsdProvider` | Integraciones.Fiscal | CSD del emisor por empresa (sello) |
| `ICfdiRepositorioPort` | Integraciones.Fiscal | Guardar/leer el `cfdi_archivo` común (Decisión 01-B) |

### 6.2 Puertos de escritura / write-back (Facturación expone o invoca)

| Puerto | Dirección | Para qué |
|---|---|---|
| `IFacturacionCfdiReadPort` | Facturación **expone** | Obras/CxC/Contabilidad consultan CFDIs por obra/cliente/UUID (sin tocar el esquema) |
| `IAwWriteBackPort` | Facturación **invoca** (Integraciones.Aw) | Escribe en la fila de `aw_solicitud_pedido`: `erp_pedido_id` (claim, al ingestar), `estado_facturacion` + `uuid` (al facturar/cancelar), `resultado`/`motivo` (al procesar cada solicitud). Esquema definido por el ERP. |
| `IOrigenesWriteBackPort` | Facturación **invoca** (Integraciones.Origenes) | Reflejar estado en Planta Pintura (Salidas no requiere write-back) `[Gap mecanismo]` |

### 6.3 Adapters de Infrastructure

- `FacturacionRepository` (EF) por agregado.
- `PdfFacturaRenderer` — plantillas React/`@react-pdf` (bilingüe ES/EN +
  simplificada térmica) conforme [ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md).
- `MapsterCfdiMapper` — dominio → DTO de `IFiscalApiClient`.
- `OutboxSaveChangesInterceptor` + `OutboxPublisherWorker<FacturacionDbContext>`.
- Stubs/NoOp (§13) para puertos cuyo dueño no existe aún.

---

## 7. Comandos y queries (CQRS)

### 7.1 Comandos (escritura)

| Comando | Efecto | Permiso |
|---|---|---|
| `ProcesarSolicitudAwCommand` | Procesa una solicitud de la cola (Alta/Modificación/Cancelación) según la matriz §12.1; resuelve cliente/artículo y **auto-provisiona desde A+W si faltan** (`IMasterProvisioningPort`); persiste/refresca/cancela `PedidoFacturable`; escribe claim/estado/resultado de vuelta (worker) | `facturacion.pedidos.importar` |
| `ImportarPedidoPlantaPinturaCommand` | Ingesta de orden de Planta Pintura (exige master preexistente; excepción si falta) | `facturacion.pedidos.importar` |
| `CrearPedidoFacturableManualCommand` | Captura un `PedidoFacturable` con `origen = Manual` + líneas inline; valida contra masters | `facturacion.pedidos.capturar` |
| `EditarPedidoFacturableManualCommand` | Edita encabezado/líneas de un pedido manual aún no facturado (ETag) | `facturacion.pedidos.capturar` |
| `ResolverExcepcionImportacionCommand` | Marca resuelta una excepción / refresca pedido | `facturacion.pedidos.excepciones.resolver` |
| `EmitirFacturaVentaCommand` | Crea + sella + timbra factura I; si lleva anticipos, autogenera + timbra NC de amortización (atómico); si bonificación, sugiere NC | `facturacion.facturas.emitir` |
| `EmitirFacturaAnticipoCommand` | Timbra factura de anticipo, crea `Anticipo` | `facturacion.anticipos.emitir` |
| `VincularAnticipoCommand` | Vincula anticipo(s) a la factura final (M2, relación 07) | `facturacion.anticipos.vincular` |
| `EmitirNotaCreditoBonificacionCommand` | Timbra NC por bonificación (relación 01) | `facturacion.notas_credito.bonificacion.emitir` |
| `EmitirCartaPorteCommand` / `CrearSiguienteTramoCommand` | Timbra Carta Porte (T/I); prellena tramo siguiente | `facturacion.carta_porte.emitir` |
| `EmitirReppCommand` | Timbra complemento de pago (P), calcula ganancia/pérdida cambiaria | `facturacion.repp.emitir` |
| `AplicarPedimentoCommand` | Incorpora pedimento desde Salidas (completa borrador o dispara sustitución) | `facturacion.facturas.emitir` |
| `AutorizarVentaActivoCommand` | Contador General autoriza antes de timbrar; valida alta como activo | `facturacion.activos.autorizar` |
| `SolicitarCancelacionCommand` | Crea `SolicitudCancelacion`, valida cadena (anticipos/NC) | `facturacion.cancelaciones.solicitar` |
| `ReenviarCfdiCorreoCommand` | Reintenta envío al cliente | `facturacion.facturas.emitir` |

### 7.2 Queries (lectura)

| Query | Devuelve |
|---|---|
| `BandejaPedidosFacturablesQuery` | Pedidos importados pendientes de facturar (filtros canal/sucursal/estado) |
| `BandejaExcepcionesImportacionQuery` | Excepciones por motivo |
| `ControlAnticiposResumenQuery` / `...DetalladaQuery` | Vistas Resumen / estado de cuenta por cliente (§6.7) |
| `LiquidacionCajaQuery` | Facturado vs. cobrado por forma de pago, NCs, diferencia CxC |
| `EstadosFacturasAnticipoQuery` | Reporte §13.3 (export PDF/Excel) |
| `CfdisPorObraQuery` | CFDIs vinculados a una Obra (para Obras) |
| `ComprobanteDetalleQuery` | Detalle + cadena de relaciones CFDI + bitácora |

Lectura vía SQL/proyecciones; los reportes devuelven el contrato JSON de
[ADR-0036](../../decisiones/0036-estrategia-de-reporteria.md)
(`{ titulo, generadoEn, filtrosAplicados, columnas, filas, totales }`).

---

## 8. Eventos de integración

Naming canónico `{Agregado}{Verbo}Event`, esquema/topic `facturacion-events`.

### 8.1 Eventos publicados por Facturación

| Evento | Suscriptor | Propósito |
|---|---|---|
| `FacturaVentaTimbradaEvent` | Contabilidad, CxC, write-back orígenes | Ingreso reconocido; saldo por cobrar; transición a 115 |
| `FacturaAnticipoTimbradaEvent` | Contabilidad | Asiento de anticipo (MXP/USD) |
| `NotaCreditoTimbradaEvent` | Contabilidad, CxC | NC (amortización/bonificación) |
| `ReciboPagoTimbradoEvent` (REPP) | Contabilidad, CxC, write-back | Cobro confirmado; ganancia/pérdida cambiaria; transición a 70 |
| `ComprobanteCanceladoEvent` | Contabilidad, CxC | Reversa fiscal/financiera |
| `AnticipoAmortizadoEvent` | Informativo (CxC) | Saldo de anticipo actualizado |
| `PedidoFacturadoEvent` / `PedidoLiquidadoEvent` | write-back a orígenes | Estatus 115 / 70 (vía `IAwWriteBackPort` / `IOrigenesWriteBackPort`) |

### 8.2 Eventos suscritos por Facturación

| Evento | Publica | Efecto |
|---|---|---|
| `PeriodoContableCerradoEvent` | Contabilidad | Actualiza candado local de períodos |
| `PagoClienteConfirmadoEvent` | Tesorería/Ingresos | Dispara `EmitirReppCommand` (automatización REPP) |
| `CancelacionSatResueltaEvent` | Integraciones.Fiscal | Resuelve `SolicitudCancelacion` (asíncrono) |

> Idempotencia en consumidores correlacionando por `uuid`, `comprobante_id`,
> `solicitud_cancelacion_id`, `pago_id`.

---

## 9. Workers en-proceso (`IHostedService` en `Millet.Api`)

| Worker | Función | Schedule |
|---|---|---|
| `AwSolicitudesWorker` | Consume la cola `aw_solicitud_pedido` en orden por (pedido, version); aplica la matriz operación×estado (Alta→ingesta, Modificación→refresh, Cancelación→cancela; con CFDI→alerta); valida contra `ingesta_control`; escribe de vuelta claim/estado/resultado | cada N min (config) |
| `PlantaPinturaImportWorker` | Pull de órdenes facturables de Planta Pintura vía `IPlantaPinturaPedidosReader` (sin auto-provisión de master) | cada N min |
| `WriteBackResultadoWorker` | Reintenta el write-back de resultado (UUID + estatus 115/70) a A+W/Origenes desde el Outbox si la escritura síncrona falló | continuo |
| `TimbradoPendienteWorker` | Resuelve `TimbradoEnProceso` consultando el PAC (FSM asíncrona, Decisión 01-C). Con [01-G G5] el pedido queda ligado a su factura, así que vencer a `PAC_TIMEOUT` ya no deja pedidos inconsistentes; el reintento/descarte ocurre sobre la factura | cada N seg/min |
| `CancelacionSatPollerWorker` | Poll de estatus de cancelaciones `EnProceso` (respuesta del receptor 3 días) | intra-día |
| `EnvioCfdiCorreoWorker` | Reintentos de envío de CFDI al cliente con backoff | continuo |
| `PedimentoSalidasWorker` | Empareja Hojas de Salida ↔ facturas de exportación y aplica pedimento | cada N min |
| `OutboxPublisherWorker<FacturacionDbContext>` | Publica eventos del Outbox a Service Bus | continuo |

Catálogos SAT: la sincronización del espejo (nocturna) vive en
`Integraciones.Fiscal`, no aquí.

---

## 10. RBAC — permisos canónicos `facturacion.*`

> **Atención (memoria [feedback_permisos_canonicos_migration]):** agregar a
> `PermisosCanonicos.Todos` afecta el modelo de `IdentidadDbContext`
> (`HasData`). Requiere una migration en Identidad en el mismo PR, o el
> deploy aborta con `PendingModelChangesWarning`.

- `facturacion.pedidos.importar`, `facturacion.pedidos.capturar`,
  `facturacion.pedidos.excepciones.resolver`
- `facturacion.facturas.emitir`, `facturacion.facturas.leer`
- `facturacion.anticipos.emitir`, `facturacion.anticipos.vincular`,
  `facturacion.anticipos.leer`
- `facturacion.notas_credito.bonificacion.emitir`,
  `facturacion.notas_credito.leer`
- `facturacion.carta_porte.emitir`, `facturacion.carta_porte.leer`
- `facturacion.repp.emitir`
- `facturacion.cancelaciones.solicitar`, `facturacion.cancelaciones.consultar`
- `facturacion.activos.autorizar` (rol Contador General)
- `facturacion.caja.liquidar` (remapeado al cierre de sesión de caja) y el
  resto del namespace `caja.*` (`administrar`, `operar`, `supervisar`,
  `leer-todas`) — ver [`12-cajas.md`](12-cajas.md) §8
- `facturacion.reportes.leer`

---

## 11. Endpoints HTTP (resumen, `/api/v1/facturacion`)

| Método | Ruta | Comando/Query |
|---|---|---|
| GET | `/pedidos-facturables` | Bandeja |
| POST | `/pedidos-facturables` | Crear pedido manual con líneas (`Idempotency-Key`) |
| PUT | `/pedidos-facturables/{id}` | Editar pedido manual no facturado (If-Match) |
| GET | `/pedidos-facturables/excepciones` | Bandeja de excepciones |
| POST | `/pedidos-facturables/excepciones/{id}/resolver` | Resolver |
| POST | `/facturas` | Emitir factura de venta (`Idempotency-Key`) |
| GET | `/facturas/{id}` | Detalle (ETag) |
| POST | `/facturas/{id}/pedimento` | Aplicar pedimento |
| POST | `/anticipos` | Emitir factura de anticipo |
| POST | `/anticipos/{id}/vincular` | Vincular a factura final |
| GET | `/anticipos/control` · `/anticipos/control/{clienteId}` | Reportes |
| POST | `/notas-credito/bonificacion` | Emitir NC bonificación |
| POST | `/carta-porte` · `/carta-porte/{id}/siguiente-tramo` | Carta Porte |
| POST | `/repp` | Complemento de pago |
| POST | `/comprobantes/{id}/cancelar` | Solicitar cancelación |
| GET | `/comprobantes/{id}/cancelar` | Estatus de cancelación |
| POST | `/activos/{facturaId}/autorizar` | Autorización Contador General |
| POST | `/comprobantes/{id}/reenviar-correo` | Reenvío |
| GET | `/reportes/liquidacion-caja` · `/reportes/estados-anticipos` | Reportes (JSON ADR-0036) |

Mutaciones con `Idempotency-Key` ([ADR-0020](../../decisiones/0020-idempotencia-http.md));
errores Problem Details ([ADR-0010](../../decisiones/0010-manejo-errores-problem-details.md));
borradores con ETag/If-Match ([ADR-0012](../../decisiones/0012-concurrencia-hibrida.md)).

Los endpoints de cajas, sesiones de efectivo y cobros de mostrador
(`/cajas/...`, `/cobros/...`) se documentan en [`12-cajas.md`](12-cajas.md) §10.

---

## 12. Frontend — patrones aplicables

Sigue [`frontend/docs/patrones-compras.md`](../../../frontend/docs/patrones-compras.md)
y la memoria de estructura de ventanas ERP.

- **Bandeja de pedidos facturables** (P2 — tabular filtrada server-side) →
  acción "Facturar" abre el flujo de emisión. Botón **"Nuevo pedido"**
  (captura manual).
- **Nuevo pedido manual** — Sheet (slide-from-right) con encabezado (cliente,
  canal, comportamiento fiscal, moneda, Obra) + **líneas inline** estilo
  `LineaInlineForm` de Requisiciones (memoria
  [feedback_inline_no_modal_para_items]). Es el exemplar de captura
  maestro-detalle de Requisiciones aplicado a Facturación (memoria
  [feedback_reutilizacion_codigo]: reusar el componente de líneas de RQ).
- **Master-detail del comprobante** (P3): lista 320px + panel detalle con
  cadena de relaciones CFDI, bitácora de envío, descargas XML/PDF.
- **Sheet (slide-from-right)** para "Nueva factura / anticipo / NC / Carta
  Porte"; confirm al cerrar con `isDirty`; `Force: true` en success.
- **Inline forms** (no modal) para líneas de la factura, mercancías de
  Carta Porte, formas de pago (estilo `LineaInlineForm` de RQ; memoria
  [feedback_inline_no_modal_para_items]).
- **Bandeja de excepciones de importación** (P2) con resolución inline.
- **Control de Anticipos** como reporte con `<ReporteShell>` (Resumen +
  Detallada por cliente).
- **Sub-topbar** del detalle sticky con `data-print="hidden"`; aside master
  `data-print="hidden"` para impresión limpia del CFDI.

---

## 13. Dependencias de plataforma pendientes

> [ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md). Cada
> `PLATFORM-TODO` en código tiene una fila aquí. Hereda y refina la tabla del
> [levantamiento §14](00-levantamiento.md#14-dependencias-de-plataforma-pendientes).

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| `FacturacionDbContext` + esquema `facturacion` | — | n/a | `Program.cs` + `MigrationsHealthCheckOptions` + `deploy-app-dev.yml` (mismo PR) |
| `IFiscalApiClient` fase 2 (timbrar/cancelar/consultar + complementos) | `<IntegracionesFiscalFase2>` | Stub que devuelve UUID/sello fake en sandbox | Implementar fase 2 de Integraciones.Fiscal (rediseño asíncrono). Facturación consume el puerto |
| `ICsdProvider` | `<IntegracionesFiscalCsd>` | Stub con CSD de pruebas SAT | Provisto por Integraciones.Fiscal; CSD en Key Vault |
| `ICfdiRepositorioPort` (repo de **emitidos**) | — (adapter real) | `CfdiArchivoRepository` en `integraciones_fiscal` (F2-PR1, **no es stub**) | Ya wired: Facturación custodia sus CFDIs **emitidos** en `integraciones_fiscal.cfdi_archivo`. **Decisión "Separados" (owner, 2026-05-30):** CxP sigue con su blob para **recibidos** (no se unifican físicamente — son documentos de negocio distintos); un repositorio unificado para conciliación/BI lo arma el módulo 10 jalando de ambos. `UuidCfdi`/`IXmlCfdiParser`/`ICfdiBlobStorage` **no** se promueven (59/7/6 usos en CxP — deuda de refactor aparte si alguna vez duele) |
| `Millet.Integraciones.Origenes` (Planta Pintura + Salidas) | `<IntegracionesOrigenes>` | Fixtures/stub readers | Nuevo módulo con Hybrid Connection a `SER-DATA`; readers de las 2 vistas |
| `IAwSolicitudesReader` | ✅ **CERRADO** (ADR-0048, PR3 #449) | `AwSolicitudesSqlReader` (Integraciones.Aw/Pedidos) sobre `MILLET_INTEGRACION.aw_solicitud_pedido` + vistas `vw_erp_pedido_*`; activo vía toggle `AwIntegracionDb`. `StubAwSolicitudesReader` queda como fallback dev | Ya wired. Vistas se afinan en la fase guiada (runbook M2) |
| `IAwWriteBackPort` (claim al ingestar + resultado UUID/estatus al facturar) | ✅ **CERRADO — mitad A+W** (ADR-0048, PR3/PR5 #449/#452) | `AwWriteBackSqlAdapter` + `WriteBackResultadoWorker` (columnas `write_back_*` de `ingesta_control` como outbox local) | Ya wired. `ingesta_control` sigue siendo fuente de verdad |
| `IOrigenesWriteBackPort` (Planta Pintura) | `<PlantaPinturaOrigenes>` | No existe aún (F10 bloqueado) | Con `Millet.Integraciones.Origenes`; acordar contrato con Planta Pintura |
| `IPeriodoContablePort` | `<PeriodoContableCerrado>` | Stub "siempre abierto" | Cuando exista Contabilidad |
| `IContabilidadAsientoPort` | — | Stub que loggea (consume del Outbox) | Cuando exista Contabilidad |
| `IClientesReadPort` / `IProductosReadPort` (fiscal) | ✅ **CERRADO** (ADR-0048, PR2 #448) | `ClientesReadAdapter` / `ProductosReadAdapter` sobre `compartido.clientes` + `compartido.producto_aw` (master de venta SEPARADO de `articulos` — decisión D5) | Ya wired dentro de `AddFacturacionModule`. UI admin en #453 |
| `ICatalogosSatReadPort` (catálogos SAT) | — (adapter real) | `CompartidoCatalogosSatReadAdapter` sobre `CompartidoDbContext` (F0-PR1, **no es stub**) | Ya wired: consulta `compartido.{formas_pago,usos_cfdi,regimenes_fiscales,monedas}` seedeados por `Millet.Catalogos` |
| `IMasterProvisioningPort` + readers de master A+W (auto-provisión cliente/artículo) | ✅ **CERRADO** (ADR-0048, PR4 #450) | `AwMasterProvisioningAdapter` (bridge en Integraciones.Aw/Pedidos) + `ProvisionarClienteDesdeAwCommand`/`ProvisionarProductoAwCommand` (DatosMaestros ejecuta el alta; upsert idempotente por referencia externa). Activo vía toggle; `NoOpMasterProvisioningPort` = fallback dev | Ya wired (§3.bis.6). Vistas `vw_erp_cliente`/`vw_erp_articulo` se afinan en fase guiada |
| `IActivosFijosReadPort` | `<ActivosFijos>` | Catálogo mínimo de activos con saldos | Para venta de activos |
| `concepto_contable_map` | — | Seed con `TBD-*` + `requiere_codigo_definitivo` | Migración de filas cuando Contabilidad entregue códigos |
| Envío de CFDI al cliente | `<EnvioCfdiCliente>` | NoOp si SMTP/Graph no configurado | Reusar Integraciones.Mailbox / `INotificacionService` |
| Outbox Facturación | — | `NoOpIntegrationEventBusSender` si cs vacía | `OutboxPublisherWorker<FacturacionDbContext>` |
| Permisos `facturacion.*` | — | Registrar al arrancar | Migration en IdentidadDbContext (memoria) |

---

## 14. Riesgos técnicos

| Riesgo | Prob. | Impacto | Mitigación |
|---|---|---|---|
| Fase 2 de Integraciones.Fiscal en rediseño bloquea timbrado real (CCE/Carta Porte) | Alta | Alto | Diseño asíncrono-tolerante (FSM) + stub `IFiscalApiClient`; construir dominio/esquema/UI sin timbre real; cierre de complementos espera a la fase 2 |
| Master fiscal de Cliente/Producto incompleto al go-live (migración SAP/A+W) | Alta | Alto | Bandeja de excepciones bloquea facturas con datos faltantes; proceso de captura previa; no se factura sin clave SAT/RFC |
| Atomicidad timbre factura final + NC de amortización con PAC asíncrono | Media | Alto | Ambos en `TimbradoEnProceso`; `TimbradoPendienteWorker` los resuelve juntos; rollback si uno falla definitivamente |
| Secuencia del pedimento (antes vs. después del timbre) | Media | Medio | **Compuerta condicional por factura** (`requiere_pedimento`), nunca global — no bloquea las facturas que no llevan pedimento. Soportar ambos caminos (`AplicarPedimentoCommand` completa borrador o dispara sustitución); confirmar con sample fiscal |
| Marcar mal `requiere_pedimento` (falsos positivos bloquean facturas; falsos negativos timbran sin pedimento obligatorio) | Media | Medio | El indicador lo aporta el pedido (columna de la vista o captura manual), **no** el catálogo de Producto; validación en captura; reporte de facturas en `PendientePedimento`; contrato de columnas debe incluirlo (§15.1) |
| Consumo de timbres en errores corregibles | Media | Bajo | Validación local previa antes de llamar al PAC |
| Doble importación de un pedido o doble facturación | Media | Alto | Unicidad `(origen, numero_pedido, version_origen)`; control de piezas (no sobrefacturar) |
| Refactor del repo común de CFDI rompe CxP | Media | Medio | Migración coordinada; arrancar con repo local de Facturación si CxP no está listo (A7) |
| Cancelación post-pago de un CFDI ya contabilizado | Media | Alto | FSM `CancelacionPendiente` + evento `ComprobanteCanceladoEvent` a Contabilidad/CxC |

---

## 15. Pendientes y próximos pasos

### 15.1 Antes del `02-plan-implementacion.md`

1. Confirmar nombre del módulo de orígenes (`Millet.Integraciones.Origenes`)
   y su esquema (A1).
2. Confirmar ubicación del repo común de CFDI (`integraciones_fiscal`) y el
   plan de migración de `CfdiRecibido` en CxP (A7, Decisión 01-B).
3. Confirmar serie de anticipos `FANT` vs `ANT` (A8).
4. Confirmar el contrato de columnas de las tres vistas SQL (levantamiento
   §16.1.4) — necesario para los readers de Aw y Origenes.
5. Recibir el sample fiscal Anticipo + Factura final + NC de amortización
   (levantamiento §16.1.2) para fijar el mapeo `CfdiRelacionados`.
6. Coordinar el cierre de la fase 2 de Integraciones.Fiscal (D1–D10 +
   sandbox) — bloqueante para timbrado real.
7. `serie` → **resuelto**: se reusa `Compartido.Series`. Falta decidir si
   `concepto_contable_map` vive en `facturacion` o en Administración/Contabilidad.

### 15.2 Diferidos a fase posterior (post-MVP)

- Factura Global periódica (modo `Pendiente de ticket`).
- NC por devolución (Inventario).
- Cuenta puente "Obras Entregadas No Facturadas".
- Complemento Nómina.
- Ticket de mostrador (facturación online).

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-29 | Diseño inicial sobre el levantamiento 1.0. Decisiones del owner: ingesta partida (A+W en Integraciones.Aw; Planta Pintura + Salidas en nuevo Integraciones.Origenes), MVP amplio con CCE + Carta Porte, repo de CFDI común desde ahora. Decisiones de diseño: timbrado asíncrono-tolerante (FSM), NC de amortización atómica, TPT para Comprobante. |
| 1.1 | 2026-07-10 | Adenda de Cajas ([`12-cajas.md`](12-cajas.md)): alcance de datos por sucursal×canal (Capa A, resolución dinámica) + sesión de efectivo con arqueo (Capa B). `caja_id` de `comprobante` queda reservado a la Capa B; namespace `facturacion.caja.*` ampliado; nuevos endpoints `/cajas` y `/cobros`. |
| 1.2 | 2026-07-12 | Adenda de Anticipos ciclo completo + trazabilidad ([`13-anticipos-ciclo-completo.md`](13-anticipos-ciclo-completo.md)): bandeja/detalle de facturas de anticipo, descargas XML/PDF genéricas (anticipo + NC) con alcance de cajas, trazabilidad transversal (tipos 5–10) y fix 13-J (`ANTICIPO_CFDI_NO_TIMBRADO` — la relación 07 es obligatoria). Absorbe el alcance diferido de la Fase 13. |
