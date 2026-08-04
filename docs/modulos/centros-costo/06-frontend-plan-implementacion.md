# Plan de implementación de frontend — Módulo Centros de Costo

> **Versión:** 0.2 · **Fecha:** 2026-07-16
> **Basado en:** [`05-frontend-diseno.md`](05-frontend-diseno.md) v0.2.

---

## 0. Cómo leer

Cada fase = 1 PR frontend (`centros-costo-fe/*`), dependiente del PR
backend indicado (numeración post-refactor,
[`03-pr-breakdown.md`](03-pr-breakdown.md)). Tipos TS desde OpenAPI
(ADR-0017) tras cada merge backend. Detalle en
[`07-frontend-pr-breakdown.md`](07-frontend-pr-breakdown.md).

---

## 1. Resumen ejecutivo

**5 PRs frontend.** Dos módulos de UI (configuración + asignación) y el
campo único "Máquina" en documentos. Lo nuevo de verdad: los modales de
CRUD contextual, el **árbol de asignación con tri-estado** (sin molde
exacto) y `MaquinaSelector`. La UI es contextual — el FE es el ÚNICO punto
de traducción Dim↔etiqueta (tabla en el 05 §0).

## 2. Prerrequisitos

- CECO-PR4 (refactor Dim) mergeado antes de FE-PR1 — el FE nace hablando
  el API nuevo (`dim1|dim2|dim3`), sin capa de compatibilidad.
- CECO-PR5 (siembra) antes de FE-PR2 (el CRUD se prueba contra el árbol
  real).
- Espejo de permisos en `permission-codes.ts` entra en FE-PR1 (ya con
  `dim3.leer-todos`).
- OJO: el CI no corre vitest de FE — gate pre-push local (build + lint +
  vitest, cwd en `frontend/`).

## 3. Fases

### FE-PR1 — Foundation (S) · requiere CECO-PR4
NavCards de los DOS módulos, rutas, guards, mirrors de permisos y
`EstatusCatalogo`, árbol de configuración read-only con vocabulario
"Dimensión N".

### FE-PR2 — CRUD de configuración (M) · requiere CECO-PR5
Pantalla única completa (05 §4.1): modales con padre heredado, advertencia
de cascada con conteos, toggle de inactivos, búsqueda, CRUD de grupos.

### FE-PR3 — Asignación (M/L) · requiere CECO-PR6
Árbol de 5 niveles con tri-estado por renglón + barra resumen por
dimensión + marcado-que-expande (05 §4.2). La pieza FE sin molde.

### FE-PR4 — "Máquina" en Almacén (M) · requiere CECO-PR7
`MaquinaSelector` + reemplazo en `NuevaSalidaSheet`: **un solo campo por
línea** (desaparece la captura de máquina destino de cabecera) + displays
legibles según CC-G4 resuelto para salidas.

### FE-PR5 — "Máquina" en Compras + barrido (M) · requiere CECO-PR8
`LineaInlineForm` de RQ + displays RQ→OC + barrido de GUIDs crudos +
hardening.

## 4. Cronograma (1 dev frontend)

Arranca tras CECO-PR4/PR-5 (semana 2 del plan backend,
[`02-plan-implementacion.md`](02-plan-implementacion.md) §5); FE va una
fase detrás. Total ~4–5 semanas solapadas.

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| El árbol tri-estado se subestima (sin molde) | Presupuestado M/L; el esqueleto lazy se copia de `ArbolCentrosCosto`/`ArbolSaldos`; solo el checkbox tri-estado + resumen es nuevo |
| Etiquetas Dim↔UI divergen entre pantallas | La tabla del 05 §0 es la única fuente; un helper de traducción por contexto en el FE, no strings sueltos |
| Tipos OpenAPI desfasados | Regenerar tras cada merge backend |
| GUIDs históricos en documentos | Cold value "No catalogado" (05 §4.3) probado con datos de millet_dev |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
| 0.2 | 2026-07-16 | Modelo Dim: dependencias renumeradas (PR4-PR8), FE-PR3 pasa a árbol tri-estado (sin molde), FE-PR4/5 con campo único "Máquina", FE-PR1 requiere el refactor. |
