# Plan de implementación de frontend — Módulo Almacén

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 1) + endpoints HTTP de [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 2).
>
> **Estado:** propuesta. Sizing en bandas.
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Sizing: **XS** 1-2d · **S** 3-5d · **M** 1-2sem · **L** 3-4sem.
- Cada fase produce algo **deployable y demoable**.
- **Política de granularidad**: PRs S-M, no XS (`feedback_pr_granularidad.md`).

---

## 1. Resumen ejecutivo

**Objetivo:** entregar UI v1 del módulo Almacén alineada con [05-frontend-diseno.md](05-frontend-diseno.md), construida contra endpoints de PRs F0–F9 del backend.

**Estrategia:**

1. **Foundation + catálogo primero** (F0 + F1): shell, sidebar, CRUD de almacenes/sub-almacenes.
2. **Operación diaria** (F2-F4): recepciones, salidas, devoluciones. El usuario opera el módulo end-to-end.
3. **Inventario físico al final** (F5) — la pantalla más compleja con la "captura sin sesgo".
4. **Reportes y cierre de mes** (F6).
5. **Hardening** (F7).

**Dependencia con backend:** cada fase consume endpoints de fases equivalentes del backend. Si backend retrasa, frontend para o usa MSW.

---

## 2. Prerrequisitos

| Prerrequisito | Estado | Detalle |
|---|---|---|
| Shell del ERP | ✅ existe | Almacén se agrega como nuevo grupo. |
| Componentes compartidos (`<MasterDetailLayout>`, etc.) | ✅ existe | Reusables. |
| Tipos generados desde OpenAPI | ✅ pipeline | Almacén se agrega automáticamente. |
| `<PermissionGuard>` | ✅ existe | Reusable. |
| `<ReporteShell>` (ADR-0036) | ⏳ a crear | Coordinar con CxP F6 si CxP arranca primero. |

---

## 3. Fases

### Fase 0 — Foundation (S)

- Estructura de carpetas `frontend/src/routes/almacen/`.
- Sidebar: agregar grupo "Almacén" con cards.
- Smoke route `/almacen` → landing con cards (sin datos reales).
- Permisos canónicos `almacen.*` consumidos por `<PermissionGuard>`.
- Tipos TS generados.

### Fase 1 — Catálogo de almacenes y sub-almacenes (S)

- Ruta `/almacen/almacenes` (admin CRUD).
- Ruta `/almacen/sub-almacenes` (admin CRUD).
- Selector global de sub-almacén en topbar (FAlm2).
- Componente `<JerarquiaAlmacenBreadcrumb>`.

**Mergeable cuando:** backend F1-PR3 listo (seed inicial aplicado).

### Fase 2 — Recepciones (M)

Walking skeleton end-to-end.

- Ruta `/almacen/recepciones` (bandeja master-detail con filtros).
- Ruta `/almacen/recepciones/$id` (detalle).
- Sheet "Nueva recepción" con selector de OC + selector variante A/B (§6.1 del 05).
- Componente `<ArticuloAutocomplete>` con `<SaldoChip>`.
- Componente `<ToleranciaIndicator>` (cantidad).
- Pre-carga automática de líneas desde OC seleccionada.
- Adjuntos drag-and-drop.

**Mergeable cuando:** backend F2-PR2 (Variante A) y F3-PR1 (Variante B) listos. Auxiliar captura recepción end-to-end.

### Fase 3 — Salidas con RQ y Vale (M)

- Ruta `/almacen/salidas` (bandeja).
- Ruta `/almacen/salidas/$id`.
- Sheet "Nueva salida" tipo carrito (§6.2 del 05) con selector RQ o Vale.
- Validación de stock en línea.
- Bandeja "Vales pendientes de regularizar" con indicador de urgencia.
- Componente `<ComprobanteSalidaPdf>` con `@react-pdf/renderer` — descarga automática al confirmar.
- Reporte interno "Salidas del día".

**Mergeable cuando:** backend F4-PR1 + F5-PR1 listos.

### Fase 4 — Devoluciones (M)

- Ruta `/almacen/devoluciones` (bandeja unificada con filtro tipo).
- Ruta `/almacen/devoluciones/internas/$id` (sub-flujo 8.A).
- Sheet "Nueva devolución interna" con selector de salida origen + estado del material.
- Ruta `/almacen/devoluciones/proveedor/$id` (sub-flujo 8.B).
- Sheet "Nueva devolución a proveedor" con autorización Dirección + evidencia.
- Bandeja "Devoluciones pendientes de NC fiscal" + dashboard de conciliación.

**Mergeable cuando:** backend F5-PR1 (interna) + F6-PR1 (a proveedor) listos.

### Fase 5 — Inventario físico (L)

Pantalla flagship del módulo. Concentra la complejidad.

- Ruta `/almacen/inventarios` (bandeja).
- Ruta `/almacen/inventarios/$id` (detalle).
- Sheet "Nuevo conteo" con tipo (rotativo/anual), sub-almacén, familia, responsable.
- **Pantalla `/almacen/inventarios/$id/captura`** (§6.3 del 05) — pantalla dedicada full-width sin mostrar cantidad teórica.
  - Componente `<ConteoLineaInput>` con autoavance.
  - Endpoint del contador **NUNCA** devuelve `cantidad_teorica` — el componente confía en el backend (no hay defensa client-side adicional, pero tests E2E validan).
- **Pantalla `/almacen/inventarios/$id/aprobacion`** (§6.4 del 05) — aprobador ve cantidad teórica + variación + bulk approve.
  - Componente `<VariacionBadge>`.
  - Bulk approve con filtro de "variaciones bajo umbral".
- Componente `<ListaConteoPdf>` con `@react-pdf/renderer` para imprimir la lista al inicio del conteo.

**Mergeable cuando:** backend F7-PR1 + F7-PR2 listos. Carlos ejecuta un conteo rotativo end-to-end.

### Fase 6 — Saldos, cierre de mes, reportes (M)

- Ruta `/almacen/saldos` (bandeja con filtros).
- Ruta `/almacen/saldos/articulo/$id` (master-detail con histórico de movimientos).
- Pantalla `/almacen/cierre-mes` (§6.6 del 05) con checklist visual.
- Rutas `/almacen/reportes/*`:
  - ALFAK-HISTORIAL-ALMACEN (PDF + Excel).
  - SAP-REPORTE-EXISTENCIA-MP-CNK.
  - Salidas / entradas del día (operativos).
- `<ReporteShell>` (si no existe ya).

**Mergeable cuando:** backend F8 listo.

### Fase 7 — Hardening de UI + accesibilidad (S)

- Auditoría a11y.
- E2E críticos (Playwright): captura sin sesgo, ciclo recepción→salida→conteo, cierre de mes.
- Performance: virtualización en bandejas grandes (saldos con 10K+ artículos).
- Polish de errores (bloqueo periodo cerrado, stock insuficiente, bloqueo inventario anual).

---

## 4. Cronograma (con 1 dev frontend)

| Mes | Fases |
|---|---|
| 1 | F0 + F1 (backend F0-F1) |
| 2 | F2 (backend F2-F3) |
| 3 | F3 + F4 (backend F4-F6) |
| 4 | F5 (backend F7) — pantalla más compleja |
| 5 | F6 (backend F8) |
| 6 | F7 + hardening |

Total ~6 meses calendario, alineado con backend.

---

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| **Backend atrasado** | MSW con shapes JSON. |
| **Captura sin sesgo subestimada** | F5 con tiempo holgado. UX testing con Carlos antes del go-live. |
| **Saldos con 10K+ artículos** lentos | Virtualización desde el primer PR de F6. |
| **Comprobante PDF lento o feo** | Iterar con `@react-pdf/renderer` temprano en F3. |
| **Carlos resistente al cambio** del Portal Millet | Demos tempranas (después de F2 y F3) con cliente. Iteración corta. |

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Plan de frontend de 7 fases. ~6 meses con 1 dev frontend.
