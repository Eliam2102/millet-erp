# PR Breakdown de frontend — Módulo Cuentas por Pagar

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 1), [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md) (Rev. 1).
>
> **Estado:** Rev. 1 — consolidado siguiendo `feedback_pr_granularidad.md` desde el inicio.
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `FE-F<fase>-PR<n>`.
- **Tamaños**: S (200–500 líneas), M (500–800). **Default S-M**.
- **Política de granularidad**: PRs consolidados; aislar solo por riesgo (lógica de seguridad UX, integración crítica).
- **Branch naming**: `cxp-fe/f<fase>-<slug>`.
- **Dependencia con backend** explícita en cada fila.

> **Auto-mode N2 activo** para branches `cxp-fe/*` (memoria `feedback_no_commits.md`, extendido 2026-05-22). Mismo patrón que backend: commit/push/PR/merge automatizado con gate de CI vía hook `.claude/hooks/validate-auto-merge.ps1`.

---

## Fase 0 — Foundation FE (1 PR · S)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F0-PR1 | `cxp-fe/f0-foundation` | **Consolidado**: estructura de rutas `routes/cxp/` + sidebar con grupo "Cuentas por Pagar" + cards de las 8 secciones + smoke route `/cxp` con landing + permisos canónicos consumidos por `<PermissionGuard>` + tipos TS regenerados (`npm run gen-types`). | F0-PR1 backend | S | Usuario con permiso entra a `/cxp` y ve landing con cards; sin permiso → redirect. |

---

## Fase 1 — CFDIs (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F1-PR1 | `cxp-fe/f1-cfdis-bandeja-y-detalle` | **Consolidado**: rutas `/cxp/cfdis` + `/cxp/cfdis/$id` + bandeja P2 con filtros server-side (estado, canal, RFC, periodo) + componente `<CfdiXmlViewer>` (XML parseado legible) + componente `<CfdiPdfPreview>` (preview inline via blob URL) + acciones sheet "Marcar duplicado" y "Descartar" + bandeja alterna "CFDIs sin capturar > 5 días". | F1-PR1 backend | M | Usuario ve bandeja paginada + abre detalle + ve XML/PDF + descarta. |

---

## Fase 2 — Captura de factura con OC (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F2-PR1 | `cxp-fe/f2-captura-factura-y-bandeja` | **Consolidado**: sheet "Capturar factura desde CFDI" (pantalla flagship §6.1 — split izquierda XML / derecha campos editables) + ruta `/cxp/facturas` (bandeja master-detail) + ruta `/cxp/facturas/$id` (detalle) + componentes `<EstadoPasivoChip>`, `<ToleranciaIndicator>`, `<SubcategoriaSnapshot>` + sub-topbar con acciones contextuales por estado + manejo de error fuera de tolerancia (modal explícito de cancelación). **PR aislado por riesgo:** pantalla flagship más compleja del módulo. | F3-PR1 backend | M | Auxiliar captura una factura con OC end-to-end; fuera de tolerancia → cancelación explícita. |

---

## Fase 3 — Revisión + evidencias + autorización (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F3-PR1 | `cxp-fe/f3-revision-y-evidencias` | **Consolidado**: ruta `/cxp/revision` (bandeja por área del usuario) + ruta `/cxp/autorizaciones-firma-pendiente` + sheets "Enviar a revisión" y "Liberar revisión" + componente `<AutorizacionInformalForm>` (upload evidencia + comentario obligatorio + flag firma pendiente) + indicadores SLA en bandejas con color por días restantes. | F4-PR1+PR2 backend | M | Auxiliar manda a revisión + responsable de área libera + Dirección autoriza con evidencia. |

---

## Fase 4 — NC + anticipos + notas de cargo + comprobaciones simples (2 PRs · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F4-PR1 | `cxp-fe/f4-nc-anticipos-notas-cargo` | **Consolidado**: rutas `/cxp/notas-credito`, `/cxp/anticipos`, `/cxp/notas-cargo` (bandejas + detalle + sheets de captura) + aplicación inline de NC/anticipo a factura desde el detalle de factura + autorización Dirección de notas de cargo con `<AutorizacionInformalForm>`. | F6-PR1+PR2 backend | M | Auxiliar captura NC, anticipos, notas de cargo end-to-end; aplica a facturas correctamente. |
| FE-F4-PR2 | `cxp-fe/f4-comprobaciones-caja-chica-aduanales` | **Consolidado**: ruta `/cxp/comprobaciones` (bandeja filtrable por tipo) + sheets "Nueva Caja Chica" y "Nueva Aduanal" + componente `<LineaComprobacionInlineForm>` (varios CFDIs/tickets) + autorización por sucursal/área (Caja Chica) o doble (Aduanales: Comercio Exterior + DF). | F7-PR1+PR2 backend | M | Auxiliar crea comprobación con 5 CFDIs + 2 tickets; autoriza correctamente. |

---

## Fase 5 — Viáticos electrónicos (1 PR · L)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F5-PR1 | `cxp-fe/f5-viaticos-electronicos` | **Consolidado**: rutas `/cxp/viaticos/mis-solicitudes` (empleado) + `/cxp/viaticos/por-aprobar` (jefe) + `/cxp/viaticos/por-revisar` (CxP) + sheet "Nueva solicitud" con `<PoliticaViaticosCheck>` (validación contra políticas en línea + indicador visual) + detalle de viático con captura de comprobación al regresar + flujo de doble autorización (jefe + DF si excede). **PR aislado por riesgo:** flujo nuevo end-to-end multi-rol (~6 pantallas). | F7-PR3 backend | L | Empleado solicita → jefe aprueba → empleado comprueba → CxP revisa → Tesorería paga/cobra. |

---

## Fase 6 — TC Empresarial (2 PRs · M+L)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F6-PR1 | `cxp-fe/f6-tc-tarjetas-y-movimientos` | **Consolidado**: ruta `/cxp/tc` (landing) + `/cxp/tc/mis-movimientos` (titular) + `/cxp/tc/movimientos` (Auxiliar — todas las tarjetas) + `/cxp/tc/tarjetas` y `/cxp/tc/tarjetas/$id` (admin) + sheet "Nuevo movimiento de TC" (con/sin CFDI) + componentes `<EstadoMovimientoChip>` (7 tipos) y `<MovimientoTcCard>`. | F7-PR4 backend | M | Titular ve sus movimientos; admin crea tarjeta + agrega usuarios autorizados; Auxiliar captura movimientos. |
| FE-F6-PR2 | `cxp-fe/f6-tc-conciliacion-y-cierre` | **Consolidado**: ruta `/cxp/tc/estados-cuenta` (bandeja) + `/cxp/tc/estados-cuenta/$id` (detalle) + **pantalla full-screen `/cxp/tc/estados-cuenta/$id/conciliacion`** (`<ConciliacionTcDual>` — vista dual líneas del banco / movimientos, §6.2) + drag-and-drop opcional + cierre con dispatch a backend + manejo de refunds + capture retroactiva inline + manejo de disputas. **PR aislado por riesgo:** componente más complejo del módulo (`<ConciliacionTcDual>`). | F7-PR5+PR6 backend | L | Auxiliar sube archivo del banco → conciliación automática + manual → cierra estado de cuenta → factura del banco generada. |

---

## Fase 7 — Reportes (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F7-PR1 | `cxp-fe/f7-reportes` | **Consolidado**: `<ReporteShell>` (si no existe ya — coordinar con Almacén) + 6 rutas de reportes (antigüedad saldos, antigüedad anticipos, cartera, CFDIs sin capturar, movimientos TC, estados cuenta TC) + cada reporte con `@react-pdf/renderer` (PDF) + `exceljs` (Excel) + vista de impresión `data-print="hidden"`. | F8-PR1+PR2 backend | M | Cada reporte visible + exportable a PDF + Excel + imprimible. |

---

## Fase 8 — Hardening (1 PR · S)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F8-PR1 | `cxp-fe/f8-hardening` | **Consolidado**: auditoría a11y (axe + revisión manual) + tests E2E críticos (Playwright: captura factura, conciliación TC, viáticos) + virtualización en bandejas grandes + polish de error states/loading/empty + browser support testing. | Todas | S | E2E pasan en CI; lighthouse score > 90; axe sin issues críticos. |

---

## Resumen de granularidad

**Total: 10 PRs** (en lugar de granular ~25-30 si fuera microscópico).

- Fase 0: 1 PR (S)
- Fase 1: 1 PR (M)
- Fase 2: 1 PR (M) — aislado por riesgo (pantalla flagship)
- Fase 3: 1 PR (M)
- Fase 4: 2 PRs (M)
- Fase 5: 1 PR (L) — aislado por riesgo (flujo nuevo multi-rol)
- Fase 6: 2 PRs (M+L) — PR2 aislado por riesgo (componente más complejo)
- Fase 7: 1 PR (M)
- Fase 8: 1 PR (S)

PRs aislados por riesgo: 3 (FE-F2-PR1, FE-F5-PR1, FE-F6-PR2). Cada uno con razón explícita.

---

## Rev.

- **2026-05-22 — v1** — PR breakdown consolidado desde el inicio (siguiendo `feedback_pr_granularidad.md`). 10 PRs en 8 fases, S-M-L (sin XS).
