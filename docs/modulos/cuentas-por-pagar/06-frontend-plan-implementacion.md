# Plan de implementación de frontend — Módulo Cuentas por Pagar

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 1) + endpoints HTTP de [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 2).
>
> **Estado:** propuesta. Sizing en bandas — calibrar contra capacidad real del dev frontend.
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Sizing: **XS** 1-2d · **S** 3-5d · **M** 1-2sem · **L** 3-4sem.
- Cada fase produce algo **deployable y demoable**.
- Fases secuenciales por dependencia técnica + del backend.
- **Política de granularidad**: PRs S-M, no XS (`feedback_pr_granularidad.md`).

---

## 1. Resumen ejecutivo

**Objetivo:** entregar UI v1 del módulo CxP alineada con [05-frontend-diseno.md](05-frontend-diseno.md), construida contra los endpoints reales del backend (PRs F0–F10 del 03-pr-breakdown).

**Estrategia:**

1. **Foundation primero** (shell + tipos generados + permisos): F0.
2. **Bandejas y detalle base** antes que features avanzados (F1–F3 sigue el orden del backend).
3. **Componentes reutilizables nuevos** en cada fase (no big-bang al inicio).
4. **TC Empresarial al final** (F5), igual que en backend — concentrar la complejidad de UI.
5. **Reportes en paralelo** una vez el ciclo principal está listo.

**Dependencia con backend:** cada fase consume endpoints de fases equivalentes del backend. Si backend retrasa, frontend para o usa mocks con MSW.

---

## 2. Prerrequisitos

| Prerrequisito | Estado | Detalle |
|---|---|---|
| Shell del ERP con sidebar + topbar | ✅ existe | Compras y Requisiciones ya están integrados. |
| `<MasterDetailLayout>`, `<SheetProvider>`, `<InlineForm>` | ✅ existe | Compartidos. |
| Tipos generados desde OpenAPI | ✅ pipeline | `npm run gen-types` ya incluye automáticamente schemas nuevos. |
| `<PermissionGuard>` | ✅ existe | Reusable. |
| `<EvidenceUploader>` | ✅ existe | De Compras. |
| `<ReporteShell>` (ADR-0036) | ⏳ a crear | Por crear en F6 de CxP o coordinado con Almacén (quien arranque primero). |
| Routing por TanStack Router file-based | ✅ existe | `/cxp/...` se agrega. |

---

## 3. Fases

### Fase 0 — Foundation del frontend (S)

- Estructura de carpetas `frontend/src/routes/cxp/`.
- Sidebar: agregar grupo "Cuentas por Pagar" con cards de las 8 secciones.
- Smoke route `/cxp` → landing con cards (sin datos reales).
- Permisos canónicos `cuentas_por_pagar.*` consumidos por `<PermissionGuard>`.
- Tipos TS generados (`npm run gen-types` después del backend F0-PR1).

### Fase 1 — Bandeja de CFDIs y captura de factura básica (M)

Slice walking skeleton end-to-end.

- Ruta `/cxp/cfdis` (bandeja P2 con filtros server-side).
- Ruta `/cxp/cfdis/$id` (detalle con preview XML + PDF).
- Componente `<CfdiXmlViewer>` (XML parseado en formato legible).
- Componente `<CfdiPdfPreview>` (preview inline via blob URL).
- Comando `MarcarCfdiDuplicado` y `Descartar` (sheets).
- Bandeja "CFDIs sin capturar > 5 días" como vista alterna.

**Mergeable cuando:** backend F1-PR1 listo. Usuario puede ver bandeja paginada + abrir detalle + ver XML + descartar.

### Fase 2 — Captura de factura con OC y bandeja de facturas (M)

- Sheet "Capturar factura desde CFDI" (la pantalla flagship, §6.1 del 05).
- Ruta `/cxp/facturas` (bandeja master-detail).
- Ruta `/cxp/facturas/$id` (detalle).
- Componente `<EstadoPasivoChip>`.
- Componente `<ToleranciaIndicator>`.
- Componente `<SubcategoriaSnapshot>`.
- Sub-topbar con acciones contextuales por estado.

**Mergeable cuando:** backend F3-PR1 listo. Auxiliar puede capturar una factura con OC end-to-end.

### Fase 3 — Workflow de revisión + evidencias + autorización (M)

- Ruta `/cxp/revision` (bandeja de facturas asignadas al área del usuario).
- Bandeja "Autorizaciones con firma pendiente".
- Sheet "Enviar a revisión" con selector de motivo y dependencia.
- Sheet "Liberar revisión" con plantillas.
- Componente `<AutorizacionInformalForm>` (upload evidencia + comentario obligatorio + flag firma pendiente).
- Componente `<EvidenceUploader>` reutilizado.
- Indicadores SLA en bandejas (color por días restantes).

**Mergeable cuando:** backend F4-PR2 listo. Auxiliar manda a revisión + responsable de área libera.

### Fase 4 — NC, anticipos, notas de cargo, comprobaciones simples (M)

Agrupa todas las pantallas de objetos fiscales no-TC.

- Ruta `/cxp/notas-credito` con captura (sheet) y aplicación a factura (inline form).
- Ruta `/cxp/anticipos` con captura y aplicación a factura.
- Ruta `/cxp/notas-cargo` con captura, autorización (DG), aplicación.
- Ruta `/cxp/comprobaciones` con tipo Caja Chica + Aduanales.
- Componente `<LineaComprobacionInlineForm>` (varios CFDIs/tickets agrupados).

**Mergeable cuando:** backend F6 + F7-PR1/PR2 listos. Auxiliar opera el flujo completo de NC + anticipos + notas de cargo + caja chica + aduanales.

### Fase 5 — Viáticos electrónicos + TC Empresarial (L)

La fase más grande del frontend. Combina viáticos (autoservicio empleado) con TC (toda la lógica del anexo).

- Ruta `/cxp/viaticos/mis-solicitudes` (empleado).
- Sheet "Nueva solicitud de viáticos" con `<PoliticaViaticosCheck>`.
- Ruta `/cxp/viaticos/por-aprobar` (jefe).
- Ruta `/cxp/viaticos/por-revisar` (CxP).
- Detalle de viático con captura de comprobación al regresar.
- Ruta `/cxp/tc/mis-movimientos` (titular).
- Ruta `/cxp/tc/movimientos` (Auxiliar — todas las tarjetas).
- Ruta `/cxp/tc/tarjetas` (admin).
- Sheet "Nuevo movimiento de TC" (con/sin CFDI).
- Ruta `/cxp/tc/estados-cuenta` (bandeja).
- **Pantalla full-screen de conciliación TC** (`<ConciliacionTcDual>` — el componente más complejo del módulo).
- Componente `<EstadoMovimientoChip>` para los 7 tipos de movimiento TC.

**Mergeable cuando:** backend F7-PR3 a F7-PR6 listos. Empleado solicita viáticos + Auxiliar maneja TC end-to-end incluyendo cierre de estado de cuenta.

### Fase 6 — Reportes (M)

- `<ReporteShell>` (si no existe ya — coordinar con Almacén).
- Rutas `/cxp/reportes/*` con todas las variantes:
  - Antigüedad de saldos
  - Antigüedad de anticipos
  - Cartera
  - CFDIs sin capturar
  - Movimientos TC pendientes de conciliar
  - Estados de cuenta TC consolidado
- Cada reporte con `@react-pdf/renderer` para PDF + `exceljs` para Excel.
- Vista de impresión con `data-print="hidden"` aplicado.

**Mergeable cuando:** backend F8 listo. Auxiliar y Dirección consumen reportes.

### Fase 7 — Hardening de UI + accesibilidad (S)

- Auditoría de a11y (axe + revisión manual).
- Tests E2E críticos (Playwright o equivalente).
- Performance: virtualización en todas las bandejas grandes.
- Browser support testing.
- Polish de error states, loading, empty states.

---

## 4. Cronograma (con 1 dev frontend)

| Mes | Fases |
|---|---|
| 1 | F0 + F1 (backend F0-F2) |
| 2 | F2 + F3 (backend F3-F5) |
| 3 | F4 (backend F6 + parte de F7) |
| 4 | F5 parte 1 (viáticos) |
| 5 | F5 parte 2 (TC empresarial) |
| 6 | F6 + F7 |

Total ~6 meses calendario, alineado con backend.

---

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| **Backend atrasado respecto a frontend** | Usar MSW (Mock Service Worker) con shapes JSON acordados; backend luego provee el endpoint real. |
| **Tipos generados rompen UI** después de cambio breaking en backend | CI corre `tsc` después de regenerar tipos; rompe build → revisión inmediata. |
| **Pantalla de conciliación TC subestimada** | F5 parte 2 con tiempo holgado (1 mes). Iterar UX con feedback temprano. |
| **Captura de factura con CFDI ineficiente** | Performance test en F2: capturar 10 facturas seguidas debe tomar <5 min total. |
| **Bandejas degradan con volumen real** | Virtualización con TanStack Table desde el inicio en bandejas grandes (CFDIs, Facturas, Movimientos TC). |

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Plan de frontend de 7 fases. ~6 meses con 1 dev frontend.
