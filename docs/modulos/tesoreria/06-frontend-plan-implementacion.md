# Plan de implementación de frontend — Módulo Tesorería / Bancos

> **Versión:** 0.1 · **Fecha:** 2026-07-14
> **Basado en:** [`05-frontend-diseno.md`](05-frontend-diseno.md) v0.1.

---

## 0. Cómo leer

Cada fase = 1 PR frontend (`tesoreria-fe/*`), dependiente del PR backend
indicado. Tipos TypeScript generados desde OpenAPI (ADR-0017) tras cada
merge backend. Detalle en [`07-frontend-pr-breakdown.md`](07-frontend-pr-breakdown.md).

---

## 1. Resumen ejecutivo

**6 PRs frontend.** El módulo es operativo-transaccional: bandejas +
Sheets + una pantalla específica (matching de conciliación). Máximo reuso
de componentes existentes (selectores, ReporteShell, badges); lo nuevo de
verdad es `CuentaBancariaSelector`, `PasivoPicker` y la pantalla de
matching.

## 2. Prerrequisitos

- Backend TES-PR1 mergeado (permisos + OpenAPI del módulo).
- App "Tesorería" dada de alta en el shell/launcher (se hace en FE-PR1).
- OJO: el CI no corre vitest de FE — correr local antes de cada merge.

## 3. Fases

### FE-PR1 — Foundation (S) · requiere TES-PR1
Rutas base, sección en launcher, guards de permisos, layout de bandejas
vacías con mensajes de arranque.

### FE-PR2 — Movimientos + pago individual (M) · requiere TES-PR4
Libro de movimientos (P1+P3), bandeja de pasivos (P2), Sheet de
Registrar pago (§4.2), reversa, `CuentaBancariaSelector` + `PasivoPicker`.

### FE-PR3 — Corridas (M) · requiere TES-PR5
Master-detail de corrida, líneas inline, flujo de autorización, oficio
PDF (`<ReporteShell>` + `@react-pdf/renderer`).

### FE-PR4 — Pago a cuenta + Ingresos (M) · requiere TES-PR6 + TES-PR7
Bandeja de pagos a cuenta + Sheet + liga tardía (§4.3); depósitos por
confirmar + confirmación/rechazo (§4.4).

### FE-PR5 — Conciliación (L) · requiere TES-PR9
Pantalla de matching (§4.1), carga de extracto, alta asistida, cierre +
acta PDF.

### FE-PR6 — REPP + reportes + hardening (S) · requiere TES-PR8 + TES-PR10
Bandeja REPP pendientes + Sheet de registro; flujo de efectivo y
auxiliares en `<ReporteShell>`; barrido de estados vacíos/errores.

## 4. Cronograma (1 dev frontend)

Arranca en la semana 2 del plan backend (ver
[`02-plan-implementacion.md`](02-plan-implementacion.md) §4); FE va una
fase detrás del backend correspondiente. Total ~6 semanas solapadas.

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| FE-PR5 (matching) subestimado | Es la única pantalla no-patrón; presupuestar L y reusar el matching de CxC como base |
| Tipos OpenAPI desfasados | Regenerar tras cada merge backend; no editar tipos a mano |
| Enums FE desincronizados del check constraint | Mirror en el mismo PR que agregue el valor (regla del repo) |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
