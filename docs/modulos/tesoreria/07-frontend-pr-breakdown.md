# PR Breakdown de frontend — Módulo Tesorería / Bancos

> **Versión:** 0.1 · **Fecha:** 2026-07-14
> **Basado en:** [`06-frontend-plan-implementacion.md`](06-frontend-plan-implementacion.md) v0.1.

---

## 0. Cómo leer

Ramas `tesoreria-fe/pr{N}-{slug}` (✅ en la allowlist del hook
`validate-auto-merge` desde 2026-07-14 — auto-mode N2). Un PR por sesión,
vitest local antes de merge (el CI no lo corre para FE).

---

## TES-FE-PR1 — Foundation (S) · `tesoreria-fe/pr1-foundation`

- Rutas `/tesoreria/*`, sección + cards en el launcher, guards por permisos `tesoreria.*`, layouts de bandeja vacía con mensajes de arranque ("los pasivos aparecen cuando CxP los autoriza").
- Mirrors FE de los enums del módulo (valores = check constraints).
- **DoD:** navegación completa visible según permisos; sin datos.

## TES-FE-PR2 — Movimientos + pago (M) · `tesoreria-fe/pr2-movimientos-pago`

- `/tesoreria/movimientos` (P1+P3: libro por cuenta, detalle con aplicaciones y contramovimiento).
- `/tesoreria/pagos` (P2: bandeja de pasivos, filtros server-side) + Sheet "Registrar pago" multi-pasivo (RN-3 en UI: cuentas filtradas por moneda) + reversa con motivo.
- Componentes nuevos: `CuentaBancariaSelector` (máscara + filtro moneda), `PasivoPicker` multi-selección.
- CLABE/cuenta enmascaradas; "ver completo" gated por `tesoreria.movimientos.ver-cuenta-completa`.
- **DoD:** ciclo pagar/revertir completo contra dev.

## TES-FE-PR3 — Corridas (M) · `tesoreria-fe/pr3-corridas`

- `/tesoreria/corridas` bandeja + master-detail; líneas **inline** (dashed/amber, nunca modal).
- Flujo Borrador→EnAutorizacion→Autorizada→Ejecutada→Cerrada con acciones por estado y ETag/If-Match (412 → recarga con aviso).
- Oficio de cartera: PDF client-side (`@react-pdf/renderer`) desde `OficioCarteraQuery`.
- **DoD:** corrida completa con oficio imprimible.

## TES-FE-PR4 — Pago a cuenta + Ingresos (M) · `tesoreria-fe/pr4-pago-cuenta-ingresos`

- `/tesoreria/pagos-cuenta`: bandeja con antigüedad, Sheet de alta (motivo obligatorio), liga tardía con banner de sugerencia y doble confirmación si el monto difiere.
- `/tesoreria/depositos`: bandeja de propuestas CxC + expectativas de Caja; confirmar (selector/alta rápida de movimiento de ingreso) y rechazar con motivo; badge de estado fiscal (`repp_timbrado`) — dejar claro que confirmar ≠ timbrado.
- **DoD:** confirmación de depósito dispara REPP en dev y el badge se actualiza al llegar el timbrado.

## TES-FE-PR5 — Conciliación (L) · `tesoreria-fe/pr5-conciliacion`

- `/tesoreria/conciliacion`: lista por cuenta/periodo + pantalla de matching dos columnas (base: matching de aplicación de pagos de CxC).
- Carga de extracto (upload por perfil), confirmación individual/lote de matches, match manual, alta asistida prellenada, header con avance y diferencia de saldo en vivo.
- Cierre bloqueado hasta cuadrar (RN-7) + acta PDF.
- **DoD:** un mes real conciliado end-to-end en dev.

## TES-FE-PR6 — REPP + reportes + hardening (S) · `tesoreria-fe/pr6-repp-reportes`

- `/tesoreria/repp`: bandeja de pagos sin REPP con antigüedad vs SLA 5 días + Sheet de registro (UUID + XML upload).
- `/tesoreria/reportes/*`: flujo de efectivo y auxiliar de bancos sobre `<ReporteShell>` (filtros, export PDF/Excel, `data-print="hidden"`).
- Hardening: barrido de estados vacíos, errores Problem Details, permisos ocultan acciones.
- **DoD:** los 2 reportes exportan; bandeja REPP libera motivo en CxP al registrar.

---

## Resumen de granularidad

| PR | Tamaño | Requiere backend |
|---|---|---|
| FE-PR1 foundation | S | TES-PR1 |
| FE-PR2 movimientos+pago | M | TES-PR4 |
| FE-PR3 corridas | M | TES-PR5 |
| FE-PR4 pago a cuenta+ingresos | M | TES-PR6, TES-PR7 |
| FE-PR5 conciliación | L | TES-PR9 |
| FE-PR6 REPP+reportes | S | TES-PR8, TES-PR10 |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
