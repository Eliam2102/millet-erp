# Deuda y diferidos del frontend — Módulo Facturación

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Doc vivo** — se actualiza en cada PR de frontend (`facturacion-fe/*`).
> Consolida todo lo que el frontend **aún no puede hacer** y por qué, para
> que ningún diferido se pierda. Complementa la §13 de
> [`05-frontend-diseno.md`](05-frontend-diseno.md) y la convención
> `PLATFORM-TODO` de [ADR-0031](../../decisiones/0031-deuda-de-plataforma-y-stubs-noop.md).

---

## 0. Cómo leer

Tres categorías, ortogonales:

- **A — Brechas de backend (bloqueantes):** el endpoint/flujo **no existe**
  en el backend mergeado; el frontend no puede implementar la feature hasta
  que llegue (normalmente **backend F3**). El FE trabaja alrededor con una
  captura mínima honesta, nunca mockeando el contrato.
- **B — Diferidos de FE (no bloqueantes):** el backend **sí** lo soporta;
  falta conectarlo desde el frontend. Es trabajo FE pendiente, no espera a
  nadie.
- **C — Diferidos por fase:** features del propio diseño/breakdown
  (`07-frontend-pr-breakdown.md`) pospuestas a una fase FE posterior por
  plan, no por brecha.

Los marcadores `PLATFORM-TODO(<id>)` en código (buscables con
`rg "PLATFORM-TODO" frontend/src/features/facturacion`) corresponden a las
filas con la columna **PLATFORM-TODO** marcada.

---

## A. Brechas de backend (bloquean FE — esperan backend, normalmente F3)

| # | Qué falta en backend | Bloquea en FE | Workaround actual | PLATFORM-TODO | Llega en |
|---|---|---|---|---|---|
| A1 | `GET /api/v1/facturacion/pedidos-facturables/{id}` (detalle con líneas + versión) | Detalle/edición de pedido (`PUT` con If-Match/ETag) y "Facturar desde pedido" con prefill de líneas | Bandeja de pedidos solo lista + crea; no hay ruta `/pedidos/$id` | — | backend F3 |
| ✅ A2 | Maestro de **clientes** + endpoint de lectura | Selector de cliente; prefill de datos fiscales del receptor en emisión | **CERRADO** (FAC-UX-PR1 #466 + PR3 + PR4): prefill desde el pedido con banner G12, y `<ClienteSelector>` (lookup `GET /facturacion/catalogos/clientes`) en emisión, pedido manual, anticipo y filtros de reportes — cero GUIDs capturados | `<ClienteSelector>` cerrado | FAC-UX-PR4 |
| ✅ A3 | **Producto fiscal** | Prefill de claves SAT + tasas por línea | **CERRADO** (FAC-UX-PR1 #466 + PR3 + PR4): prefill desde el master + `<ProductoAwSelector>` por concepto (lookup `GET /facturacion/catalogos/productos-aw`) con autollenado de claves/objetoImp/tasas | `<ProductoSelector>` cerrado | FAC-UX-PR4 |
| A4 | **Emitir-desde-pedido**: el handler `EmitirFacturaVentaHandler` fija `pedidoFacturableId: null` y no llama `PedidoFacturable.MarcarFacturado(...)` | Ligar la factura al pedido origen; transición del pedido a `Facturado`; bandeja de pedidos no refleja "facturado" | Emisión **standalone** (no ligada a pedido) | — | backend F3 |

> **Regla:** ninguna de estas se mockea en el front. Si el contrato real
> difiere al llegar, solo cambia el adaptador/hook, no la pantalla.

---

## A-bis. Solicitud de backend para FE-F2 (⏸ frontend PAUSADO)

> **Solicitud maestra a backend (todas las fases FE):**
> [`11-solicitud-backend-frontend.md`](11-solicitud-backend-frontend.md)
> audita FE-F0→FE-F10 y lista los huecos B1–B16. Esta sección A-bis es el
> subconjunto de FE-F2 (= B4–B8 de ese doc).

**FE-F2** (detalle enriquecido del comprobante) está **pausado** por decisión
del owner (2026-05-30): ~90% de sus entregables no tienen endpoint en el
backend mergeado. Solo `POST /facturas/{id}/reenviar-correo` existe; falta lo
demás. Estos son los endpoints/contratos que el backend debe exponer para
desbloquear FE-F2. El frontend reanuda en cuanto estén disponibles.

| # | Endpoint propuesto | Propósito | Contrato propuesto (response) | Permiso | Depende de |
|---|---|---|---|---|---|
| F2-BE-1 | Extender `GET /api/v1/facturacion/facturas/{id}` con `Relaciones[]` (o `GET /facturas/{id}/relaciones`) | **Cadena de relaciones CFDI** (07 anticipo / 01 NC / 04 sustitución) | `Relaciones: [{ tipoRelacion: "07"\|"01"\|"04"\|…, uuidRelacionado, folio?, tipoComprobante, total, fecha }]` | `facturacion.facturas.leer` | Existe `RelacionCfdi` en dominio (comentario del `ComprobanteDetalleQuery` dice "se incorpora en F2") |
| F2-BE-2 | `GET /api/v1/facturacion/pedidos-facturables/{id}/comprobantes` | **Historial de comprobantes del pedido** (cancelados + vigente) | `[{ id, folio, estado, uuid?, total, moneda, fechaTimbrado?, vigente: bool }]` | `facturacion.facturas.leer` | A1 (GET pedido) + A4 (liga factura↔pedido) |
| F2-BE-3 | `GET /api/v1/facturacion/facturas/{id}/envios` | **Bitácora de envío** de correo | `[{ id, destinatario, estado, fechaEnvio, error? }]` | `facturacion.facturas.leer` | Ya existe la tabla bitácora (`ReenviarCfdiCorreoResponse.BitacoraId`); falta el GET |
| F2-BE-4 | `GET /api/v1/facturacion/facturas/{id}/xml` | **Descarga XML** del CFDI timbrado | `text/xml` + `Content-Disposition: attachment` (en dev, el XML del stub) | `facturacion.facturas.leer` | — |
| F2-BE-5 | `GET /api/v1/facturacion/facturas/{id}/pdf?formato=completo\|termica&idioma=es\|bilingue` | **Descarga PDF** (completo bilingüe ES/EN; térmica solo español) | `application/pdf` + `Content-Disposition` | `facturacion.facturas.leer` | Existe `QuestPdfFacturaGenerator`; falta servirlo por HTTP |

> **Disponible hoy (FE-F2 podría usar sin backend nuevo):** `POST /facturas/{id}/reenviar-correo` (`{ destinatario }` → `{ bitacoraId, estado }`) y el print CSS client-side. Quedaron sin entregar al pausar F2; se retoman con el resto.

---

## B. Diferidos de FE (el backend ya lo soporta — falta conectarlo)

| # | Qué falta conectar | Backend disponible | Workaround actual | Acción FE |
|---|---|---|---|---|
| ✅ B1 | Selectores de catálogo **SAT** para `UsoCfdi`, `FormaPago`, `Moneda` en la emisión | Catálogos existen y el backend los **valida** (`CompartidoCatalogosSatReadAdapter`) | — | **CERRADO** (FAC-DET-PR3, = B16 de la solicitud): `<UsoCfdiSelector>` + `<FormaPagoSelector>` nuevos (molde `MonedaSelector`) + `<MonedaSelector>` reutilizado, cableados en `TabEncabezado` de emisión |

---

## C. Diferidos por fase (features del diseño pospuestas por plan)

| # | Feature | Fase FE objetivo | Nota |
|---|---|---|---|
| C1 | Cobro **multi-forma** (efectivo/transferencia/tarjeta) | FE-F9 | Es concepto de **caja/liquidación**; el CFDI lleva una sola `FormaPago`. La emisión captura una forma de pago. |
| ✅ C2 | CadenaCfdi / historial / bitácora | FE-F2 (#373) | **Entregado**: cadena de relaciones + bitácora de envío en el detalle; historial por pedido ya en FE-F1-PR3. |
| ✅ C3 | Descargas XML/PDF + reenviar correo + print | FE-F2 (#373) | **Entregado**: descargas XML/PDF (completo/térmica), reenviar correo inline, print CSS. |
| ✅ C4 | `<EmisorDefaults>`: prefill RFC/régimen del emisor desde la empresa activa | FAC-UX-PR1/PR3 | **Entregado**: `GET /api/v1/facturacion/emisor-defaults` (RFC + régimen + sucursal única activa) + `useEmisorDefaults()` prellenan el form de emisión. PLATFORM-TODO cerrado. |
| C5 | Bandeja de **excepciones de ingesta** con resolución inline | FE-F3 | `GET /pedidos-facturables/excepciones` + `POST /excepciones/{id}/resolver` ya existen. **Buildable.** |

---

## Estado por PR

> ✅ **Plan FE-F0 → FE-F10 COMPLETO** (#361–#385). Backend cerró B1–B13
> (#366–#369). ✅ **Serie FAC-UX COMPLETA** (#466–#477): emisión in-place con
> pestañas, prellenado fiscal, pickers anti-GUID (cierra B14/B15),
> master-detail Outlook. ✅ **Serie FAC-DET** (#479/#480/PR3): catálogos SAT
> en vivo vía FiscalAPI + IVA de empresa/documento + selectores B16.
> **Diferidos vigentes:** B10 (reparto REPP — sin contrato de negocio),
> F12 (timbrado real — Integraciones.Fiscal fase 2).

| PR | Entregó |
|---|---|
| FE-F0 (#361) | Foundation |
| FE-F1 (#362/#370/#371/#372) | Pedidos: bandeja, captura, detalle, edición, facturar-desde-pedido |
| FE-F2 (#363/#373) | Emisión (captura directa) + detalle CFDI enriquecido (cadena, envíos, XML/PDF) |
| FE-F3 (#374) | Bandeja de excepciones de ingesta |
| FE-F4 (#375/#376) | Anticipos: emisión + amortización + Control de Anticipos |
| FE-F5 (#377) | NC bonificación + cancelación SAT 4.0 |
| FE-F6 (#378) | REPP (bandeja + detalle + emisión) — reparto/B10 diferido |
| FE-F7 (#379/#380) | Pedimento (PendientePedimento) + CCE en exportación |
| FE-F8 (#381/#382) | Carta Porte + crear siguiente tramo |
| FE-F9 (#383/#384) | Reportes (Liquidación de caja, Estados de anticipo) + activos |
| FE-F10 (#385) | Hardening: Ayuda del módulo + test a11y (axe wcag21aa) |
| FAC-UX-PR1→PR8 (#466/#468/#470/#472/#473/#474/#475/#477) | Prellenado fiscal (BE+FE), emisión con pestañas in-place (sin Sheet), pickers anti-GUID (`<ClienteSelector>`/`<ProductoAwSelector>`/`<AnticipoPicker>`/`<AutorizacionPicker>` — cierra B14/B15), master-detail Outlook en Pedidos/Facturas/REPP/Carta Porte, tabs en detalle, fix forceMount |
| FAC-DET-PR1 (#479) | Backend: búsqueda de catálogos SAT en vivo vía FiscalAPI (`GET /api/v1/catalogos/sat/*`, 503 degradable) |
| FAC-DET-PR2 (#480) | Backend: IVA default de empresa + IVA de documento A+W (brutos→neto en ingesta) |
| FAC-DET-PR3 | FE: `<ClaveSatSelector>` (typeahead SAT en vivo + modo 503→captura manual) en productos A+W; IVA default de empresa en admin + emisión (adiós `0.16` hardcodeado); B16: `<UsoCfdiSelector>`/`<FormaPagoSelector>`/`<MonedaSelector>` en emisión |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Tracker inicial tras FE-F1 (consolida brechas A1–A4, diferido FE B1, diferidos por fase C1–C5). |
| 2.0 | 2026-05-30 | Cierre del plan FE-F0→F10 (#361–#385). Estado por PR actualizado; diferidos vigentes: B10, B14/B15, B16, F12. |
| 3.0 | 2026-07-08 | Series FAC-UX (#466–#477) y FAC-DET (#479/#480/PR3) al Estado por PR. B1/B16 ✅ (selectores SAT en emisión), B14/B15 ✅ (pickers FAC-UX-PR4). Diferidos vigentes: B10, F12. |
| 3.1 | 2026-07-12 | La bandeja/detalle de facturas de anticipo (endpoints backend sin consumidor desde F4) deja de ser deuda invisible: planificada en [`13-anticipos-ciclo-completo.md`](13-anticipos-ciclo-completo.md) (ANT-PR2), junto con descargas XML/PDF de anticipo/NC y trazabilidad transversal (ANT-PR3). Diferidos vigentes: B10, F12, página propia de NC (doc 13 §1 no-alcance). |
