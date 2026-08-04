# PR Breakdown de frontend — Módulo Almacén

> **Construido sobre:** [05-frontend-diseno.md](05-frontend-diseno.md) (Rev. 1), [06-frontend-plan-implementacion.md](06-frontend-plan-implementacion.md) (Rev. 1).
>
> **Estado:** Rev. 1 — consolidado siguiendo `feedback_pr_granularidad.md` desde el inicio.
> **Fecha:** 2026-05-22.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `FE-F<fase>-PR<n>`.
- **Tamaños**: S (200–500), M (500–800). **Default S-M**.
- **Política de granularidad**: PRs consolidados; aislar solo por riesgo.
- **Branch naming**: `almacen-fe/f<fase>-<slug>`.

> **Auto-mode N2 activo** para branches `almacen-fe/*` (memoria `feedback_no_commits.md`, extendido 2026-05-22). Mismo patrón que backend: commit/push/PR/merge automatizado con gate de CI vía hook `.claude/hooks/validate-auto-merge.ps1`.

---

## Fase 0 — Foundation FE (1 PR · S)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F0-PR1 | `almacen-fe/f0-foundation` | **Consolidado**: estructura `routes/almacen/` + sidebar con grupo "Almacén" + cards de las 7 secciones + smoke route `/almacen` + permisos `almacen.*` consumidos por `<PermissionGuard>` + tipos TS regenerados. | F0-PR1 backend | S | Usuario con permiso ve landing con cards. |

---

## Fase 1 — Catálogo de almacenes y sub-almacenes (1 PR · S)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F1-PR1 | `almacen-fe/f1-catalogo` | **Consolidado**: rutas `/almacen/almacenes` y `/almacen/sub-almacenes` (admin CRUD) + selector global de sub-almacén en topbar (FAlm2) + componente `<JerarquiaAlmacenBreadcrumb>`. | F1-PR3 backend | S | Admin crea almacén + sub-almacenes; selector global filtra bandejas. |

---

## Fase 2 — Recepciones (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F2-PR1 | `almacen-fe/f2-recepciones` | **Consolidado**: rutas `/almacen/recepciones` (bandeja master-detail con filtros) + `/almacen/recepciones/$id` (detalle) + sheet "Nueva recepción" (§6.1) con selector de OC y variante A/B + pre-carga automática de líneas + componentes `<ArticuloAutocomplete>` y `<SaldoChip>` y `<ToleranciaIndicator>` + adjuntos drag-and-drop + manejo de fuera de tolerancia (modal con autorización). | F2-PR2 + F3-PR1 backend | M | Auxiliar captura recepción Variante A y B end-to-end. |

---

## Fase 3 — Salidas con RQ y Vale (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F3-PR1 | `almacen-fe/f3-salidas` | **Consolidado**: ruta `/almacen/salidas` (bandeja) + `/almacen/salidas/$id` + sheet "Nueva salida" tipo carrito (§6.2) con selector RQ o Vale + validación de stock en línea + bandeja "Vales pendientes de regularizar" con indicador de urgencia + componente `<ComprobanteSalidaPdf>` con descarga automática + reporte interno "Salidas del día". | F4-PR1 + F5-PR1 backend | M | Almacenista surte 120 salidas/día rápidamente; comprobante PDF se genera automáticamente. |

---

## Fase 4 — Devoluciones (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F4-PR1 | `almacen-fe/f4-devoluciones` | **Consolidado**: ruta `/almacen/devoluciones` (bandeja unificada con filtro tipo) + `/almacen/devoluciones/internas/$id` (sub-flujo 8.A) + `/almacen/devoluciones/proveedor/$id` (sub-flujo 8.B) + sheet "Nueva devolución interna" con selector de salida origen + sheet "Nueva devolución a proveedor" con autorización Dirección + bandeja "Devoluciones pendientes de NC fiscal" + dashboard de conciliación. | F5-PR1 + F6-PR1 backend | M | Almacenista captura devolución interna; supervisor inicia devolución a proveedor; conciliación con NC fiscal funciona. |

---

## Fase 5 — Inventario físico (2 PRs · M)

> Aislados por riesgo: la "captura sin sesgo" requiere lógica de seguridad UX validada con tests E2E.

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F5-PR1 | `almacen-fe/f5-captura-sin-sesgo` | **Consolidado**: ruta `/almacen/inventarios` (bandeja) + `/almacen/inventarios/$id` (detalle) + sheet "Nuevo conteo" con tipo (rotativo/anual), sub-almacén, familia, responsable + **pantalla `/almacen/inventarios/$id/captura`** (§6.3) — pantalla dedicada full-width sin mostrar cantidad teórica + componente `<ConteoLineaInput>` con autoavance + lista de conteo PDF imprimible (`<ListaConteoPdf>`). **PR aislado por riesgo:** lógica de seguridad UX (captura sin sesgo). | F7-PR1 backend | M | Contador captura sin ver teórico (E2E con permiso de contador valida); siguiente línea autoavance. |
| FE-F5-PR2 | `almacen-fe/f5-aprobacion-y-aplicar` | **Consolidado**: **pantalla `/almacen/inventarios/$id/aprobacion`** (§6.4) — aprobador ve cantidad teórica + variación + bulk approve + componente `<VariacionBadge>` + filtros de aprobación + acción "Enviar a aplicar" + modal de confirmación para variaciones grandes (>$10K) + componente `<BloqueoInventarioAnualBanner>` para conteos anuales. | F7-PR2+PR3 backend | M | Aprobador aprueba bulk variaciones bajo umbral; rechaza/recuenta variaciones grandes; aplica → movimientos generados. |

---

## Fase 6 — Saldos + cierre de mes + reportes (1 PR · M)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F6-PR1 | `almacen-fe/f6-saldos-cierre-reportes` | **Consolidado**: ruta `/almacen/saldos` (bandeja con filtros) + `/almacen/saldos/articulo/$id` (master-detail con histórico) + pantalla `/almacen/cierre-mes` (§6.6) con checklist visual + `<ReporteShell>` (si no existe ya — coordinar con CxP) + rutas `/almacen/reportes/*` (ALFAK-HISTORIAL, MP-CNK, salidas del día, entradas del día) cada una con PDF + Excel. | F8-PR1+PR2 backend | M | Carlos ve saldos + ejecuta cierre de mes + genera reportes en PDF/Excel. |

---

## Fase 7 — Hardening (1 PR · S)

| ID | Título | Alcance | Backend dep | Tamaño | Mergeable cuando |
|---|---|---|---|---|---|
| FE-F7-PR1 | `almacen-fe/f7-hardening` | **Consolidado**: auditoría a11y + tests E2E críticos (captura sin sesgo con permiso de contador, ciclo recepción→salida→conteo, cierre de mes) + virtualización en bandejas grandes (saldos 10K+ artículos) + polish de errores (stock insuficiente, bloqueo anual, periodo cerrado) + browser support testing. | Todas | S | E2E pasan; lighthouse > 90; axe sin críticos. |

---

## Resumen de granularidad

**Total: 8 PRs** (en lugar de granular ~15-20 si fuera microscópico).

- Fase 0: 1 PR (S)
- Fase 1: 1 PR (S)
- Fase 2: 1 PR (M)
- Fase 3: 1 PR (M)
- Fase 4: 1 PR (M)
- Fase 5: 2 PRs (M) — PR1 aislado por riesgo (lógica de seguridad UX)
- Fase 6: 1 PR (M)
- Fase 7: 1 PR (S)

PRs aislados por riesgo: 1 (FE-F5-PR1 — captura sin sesgo).

---

## Rev.

- **2026-05-22 — v1** — PR breakdown consolidado desde el inicio. 8 PRs en 7 fases, S-M (sin XS, sin L).
