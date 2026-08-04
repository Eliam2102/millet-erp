# Solicitud consolidada a backend — para completar el frontend de Facturación

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Fecha:** 2026-05-30. **Estado:** ✅ backend cerró **B1–B9, B11–B13**
> (PRs #366–#369) y el **frontend completó el plan FE-F0→F10** (#361–#385).
> **Pendientes (no bloquean el plan ya entregado):** **B10** (reparto —
> falta contrato de negocio del área), **B14/B15** (cliente/producto —
> bloqueados por módulo `DatosMaestros` inexistente; el FE mockea los
> selectores por GUID entretanto), **B16** (catálogos SAT — ya existen en
> `GET /api/v1/catalogos/*`, es trabajo FE: cambiar texto libre por
> comboboxes). Timbrado real (F12) sigue contra el stub hasta
> `Integraciones.Fiscal` fase 2.
> Auditoría de **todo el plan FE** (`07-frontend-pr-breakdown.md`, FE-F0→FE-F10)
> contra los endpoints mergeados. Complementa el tracker
> [`10-frontend-deuda-y-diferidos.md`](10-frontend-deuda-y-diferidos.md).

---

## 0. Resumen ejecutivo

Inventario actual de endpoints de `Millet.Facturacion` (rutas mergeadas):

- **GET (lectura):** `pedidos-facturables/` (bandeja), `pedidos-facturables/excepciones`,
  `facturas/` (bandeja), `facturas/{id}` (detalle, **sin relaciones**),
  `comprobantes/{id}/cancelar` (estatus), `anticipos/control`,
  `reportes/{liquidacion-caja,estados-anticipos,cfdis-por-obra/{obraId}}`.
- **POST/PUT (escritura):** emisión factura, pedido manual (POST+PUT),
  resolver excepción, anticipo + vincular, NC bonificación, REPP,
  carta-porte (+ siguiente-tramo, vehículos, operadores), comprobante cancelar,
  pedimento, reenviar-correo, activos/autorizar.

**Diagnóstico por fase FE:**

| Fase FE | ¿Construible hoy? | Falta de backend |
|---|---|---|
| FE-F0 Foundation | ✅ Hecho (#361) | — |
| FE-F1 Pedidos + emisión | ✅ Hecho (#362/#363) | Detalle/edición de pedido y "facturar desde pedido" quedaron fuera → **B1, B2, B3** |
| FE-F2 Detalle CFDI enriquecido | ❌ Bloqueado (~90%) | **B4, B5, B6, B7, B8** |
| FE-F3 Bandejas de ingesta | ✅ Construible | — (depende de workers de ingesta para que existan excepciones, pero la UI lee lo que haya) |
| FE-F4 Anticipos | ✅ Construible | Verificar **B12** (estado de cuenta por cliente) |
| FE-F5 NC bonificación + cancelación | ⚠️ Mayormente | Re-facturación tras cancelar depende de **B2/B3** (liga pedido↔factura) |
| FE-F6 REPP + reparto | ❌ Bloqueado (lectura) | **B9** (GET REPP) + **B10** (reparto/liquidación de ruta) |
| FE-F7 Exportación/CCE + pedimento | ✅ Construible | — (CCE en el command de emisión; `pedimento` y filtro `PendientePedimento` existen) |
| FE-F8 Carta Porte | ❌ Bloqueado (lectura) | **B11** (GET bandeja + detalle de Carta Porte) |
| FE-F9 Activos + reportes | ⚠️ Mayormente | **B13** (GET activos pendientes de autorización); reportes ✅ |
| FE-F10 Hardening | ✅ Sin backend | — |

> **Listas para reanudar sin nada de backend nuevo:** FE-F3, FE-F4, FE-F7 y
> FE-F10. Si se prefiere mantener el orden del plan, lo que desbloquea más es
> resolver primero **B1–B8** (pedido + detalle CFDI).

---

## 1. Endpoints y campos solicitados

Convención: todos bajo `/api/v1/facturacion/...`, permiso indicado, RFC 7807
en errores. Contratos *propuestos* — ajustar nombres a la convención del backend.

### Bloque pedido ↔ factura (cierra FE-F1 y habilita F2/F5)

| ID | Solicitud | Contrato propuesto | Permiso |
|---|---|---|---|
| **B1** | `GET /pedidos-facturables/{id}` — detalle del pedido con líneas + versión (para detalle/edición y prefill al facturar) | `{ id, numeroPedido?, origen, estado, clienteId, clienteNombre, canalVenta, comportamientoFiscal, moneda, obraId?, obraNombre?, comentarios?, version, lineas:[{ productoId?, productoDescripcion, claveProdServSat?, claveUnidadSat?, cantidad, precio, descuento, requierePedimento }] }` + `ETag` | `facturacion.facturas.leer` |
| **B2** | **Emitir desde pedido**: que `POST /facturas` acepte `pedidoFacturableId` y, al timbrar, ligue la factura al pedido | `EmitirFacturaVentaCommand.pedidoFacturableId?: Guid` | `facturacion.facturas.emitir` |
| **B3** | Al emitir/cancelar, **transicionar el estado del pedido** (`Importado`→`Facturado`; al cancelar el CFDI vuelve a `Importado`, re-facturable) | (efecto de dominio; visible en `GET /pedidos-facturables/`) | — |

### Bloque FE-F2 — detalle del comprobante (todo bloqueado salvo reenviar-correo)

| ID | Solicitud | Contrato propuesto | Permiso |
|---|---|---|---|
| **B4** | Cadena de relaciones CFDI en el detalle (extender `GET /facturas/{id}` con `relaciones[]` o `GET /facturas/{id}/relaciones`) | `relaciones:[{ tipoRelacion:"07"|"01"|"04"|…, uuidRelacionado, folio?, tipoComprobante, total, fecha }]` | `facturacion.facturas.leer` |
| **B5** | `GET /pedidos-facturables/{id}/comprobantes` — historial de comprobantes del pedido (cancelados + vigente) | `[{ id, folio, estado, uuid?, total, moneda, fechaTimbrado?, vigente:bool }]` | `facturacion.facturas.leer` |
| **B6** | `GET /facturas/{id}/envios` — bitácora de envío de correo (la tabla ya existe: `ReenviarCfdiCorreoResponse.bitacoraId`) | `[{ id, destinatario, estado, fechaEnvio, error? }]` | `facturacion.facturas.leer` |
| **B7** | `GET /facturas/{id}/xml` — descarga del XML timbrado | `text/xml` + `Content-Disposition: attachment` | `facturacion.facturas.leer` |
| **B8** | `GET /facturas/{id}/pdf?formato=completo|termica&idioma=es|bilingue` — PDF (completo bilingüe ES/EN; térmica solo español). Ya existe `QuestPdfFacturaGenerator`, falta servirlo | `application/pdf` + `Content-Disposition` | `facturacion.facturas.leer` |

### Bloque FE-F6 — REPP + reparto

| ID | Solicitud | Contrato propuesto | Permiso |
|---|---|---|---|
| **B9** | `GET /repp/` (bandeja) + `GET /repp/{id}` (detalle) — hoy solo existe `POST /repp` | bandeja `[{ id, folio, estado, uuid?, receptorNombre, totalPagos, fecha }]`; detalle `{ …, facturasCubiertas:[{ facturaId, folio, uuid, importePagado, saldoInsoluto }] }` | `facturacion.facturas.leer` |
| **B10** | Reparto / **liquidación de ruta**: endpoint(s) para conciliar cobros por cliente en una ruta (no existe ninguno). Definir contrato de negocio con el área | (a definir) | `facturacion.caja.liquidar` (?) |

### Bloque FE-F8 — Carta Porte

| ID | Solicitud | Contrato propuesto | Permiso |
|---|---|---|---|
| **B11** | `GET /carta-porte/` (bandeja) + `GET /carta-porte/{id}` (detalle) — hoy solo existen POST (crear, siguiente-tramo, vehículos, operadores), no hay lectura | bandeja `[{ id, folio, estado, uuid?, tipo:"T"|"I", tramo, fecha }]`; detalle con vehículo, operador, mercancías y referencia al tramo previo | `facturacion.carta-porte.leer` |

### Bloque FE-F9 — Activos

| ID | Solicitud | Contrato propuesto | Permiso |
|---|---|---|---|
| **B13** | `GET /activos/autorizaciones?estado=pendiente` — bandeja del Contador General (hoy solo `POST /activos/autorizar`) | `[{ id, descripcion, monto, solicitante, fecha, estado }]` | `facturacion.activos.autorizar` |

### Datos maestros / puertos fiscales (transversal, ya en el tracker como A2/A3)

| ID | Solicitud | Nota |
|---|---|---|
| **B14** | Maestro de **clientes** + endpoint de lectura (RFC, régimen, CP, uso CFDI default, país). Hoy `IClientesReadPort` es NoOp | Desbloquea `<ClienteSelector>` y prefill de receptor. Origen: auto-provisión A+W |
| **B15** | **Producto fiscal**: `IProductosReadPort` real (clave ProdServ SAT, clave unidad, ObjetoImp, tasas IVA/retención por producto) | Desbloquea `<ProductoSelector>` y prefill de claves/tasas por línea |

### Verificaciones (no necesariamente nuevo desarrollo)

| ID | A verificar |
|---|---|
| **B12** | ¿`GET /anticipos/control` soporta detalle/filtro **por cliente** (Control de Anticipos "Detallada")? Si no, agregar `GET /anticipos/{clienteId}` (estado de cuenta). |
| **B16** | Catálogos SAT `UsoCfdi`/`FormaPago`/`Moneda`: confirmar endpoints de **listado** consumibles por el FE (probablemente ya en `admin/catalogos`). Si existen, es trabajo FE (no backend). |

---

## 2. Prioridad sugerida (para no romper el orden del plan FE)

1. **B1–B3** (pedido↔factura) — cierra FE-F1 completo y habilita F5.
2. **B4–B8** (FE-F2) — el siguiente PR del plan.
3. **B14/B15** (maestros cliente/producto) — mejoran F1/F2 y son prerequisito de varias fases.
4. **B11** (Carta Porte lectura) — FE-F8.
5. **B9/B10** (REPP + reparto) — FE-F6.
6. **B13** (activos pendientes) — FE-F9.

Mientras backend avanza, el frontend **puede reanudar en paralelo** FE-F3, FE-F4
y FE-F7 (no requieren nada nuevo), si el owner prefiere no quedar bloqueado.

---

## 3. Estado de atención (backend)

Resuelto en 3 PRs (modo auto, todos mergeados a `main`):

| PR | Items | Endpoints entregados |
|---|---|---|
| **#366** | B1, B2, B3, B5 | `GET /pedidos-facturables/{id}` (+ETag), emisión acepta `pedidoFacturableId` + transición de estado, `GET /pedidos-facturables/{id}/comprobantes` |
| **#367** | B4, B6, B7, B8 | `relaciones[]` en `GET /facturas/{id}`, `GET /facturas/{id}/envios`, `…/xml`, `…/pdf?formato=` |
| **#368** | B9, B11, B12, B13 | `GET /repp` + `/repp/{id}`, `GET /carta-porte` + `/carta-porte/{id}`, `GET /anticipos/control/{clienteId}`, `GET /activos/autorizaciones` |

**Pendientes (no construibles ahora):**
- **B14 / B15** — maestro de clientes/productos: requiere el módulo **DatosMaestros** (hoy `IClientesReadPort`/`IProductosReadPort` son NoOp, `PLATFORM-TODO<DatosMaestrosFiscal>`). El FE puede trabajar con un mock del `<ClienteSelector>`/`<ProductoSelector>` entretanto.
- **B10** — reparto / liquidación de ruta: **sin contrato de negocio**; requiere definición con el área antes de codificar.
- **B16** — catálogos SAT (`UsoCfdi`/`FormaPago`/`Moneda`): **trabajo FE**, ya existen en `GET /api/v1/catalogos/*`.
- **Timbrado real (F12)** — sigue contra `StubFiscalApiClient` hasta que cierre `Integraciones.Fiscal` fase 2.

> **El FE puede reanudar FE-F1→FE-F9** con lo entregado; sólo los selectores de cliente/producto (B14/B15) y el reparto de ruta (B10) quedan a la espera.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Solicitud inicial tras auditoría FE-F0→FE-F10. |
| 1.1 | 2026-05-30 | §3: atendidos B1–B9/B11–B13 (PRs #366/#367/#368). Quedan B14/B15 (DatosMaestros), B10 (reparto), B16 (trabajo FE). |
| 1.2 | 2026-07-12 | Solicitudes nuevas del ciclo de anticipos (detalle enriquecido, XML/PDF de anticipo/NC, árbol de trazabilidad, `FacturaAnticipoId`/`EstadoCfdi` en reportes) — **cubiertas por diseño** en [`13-anticipos-ciclo-completo.md`](13-anticipos-ciclo-completo.md) §4/§5 (ANT-PR1/PR3); no se abren B-nuevos. |
