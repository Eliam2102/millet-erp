# PR Breakdown de frontend — Módulo Cuentas por Cobrar

> **Versión:** v0.1 (borrador)
> **Fecha:** 2026-07-13
> **Depende de:** [`06-frontend-plan-implementacion.md`](06-frontend-plan-implementacion.md)

---

## 0. Cómo leer

Un PR por fase FE (consolidados, no microscópicos). Ramas `cxc-fe/pr{N}-{slug}`.
DoD común: eslint (`--no-cache`), vitest **local** (CI no lo corre), permisos
espejeados, estados loading/error/vacío, mobile drill-down del master-detail.

## CXC-FE-PR1 — Foundation (S) · `cxc-fe/pr1-foundation`
Card en nav shell (sección Finanzas), ruta `/cxc` landing, guards por permisos
`cuentas_por_cobrar.*`, client de API + tipos de líneas de crédito.

## CXC-FE-PR2 — Líneas de crédito (M) · `cxc-fe/pr2-lineas-credito`
Bandeja P1 + detalle P3 + sheet "Nueva línea" + bloquear/desbloquear +
`<CreditoDisponibleCard>` con badge `datoIncompleto`.

## CXC-FE-PR3 — Liberaciones (M) · `cxc-fe/pr3-liberaciones`
Bandeja P2 + panel de decisión (Liberar/Retener/Override) + CRUD de
autorizaciones consumibles.

## CXC-FE-PR4 — Cobranza (S) · `cxc-fe/pr4-cobranza`
`<TimelineCobranza>` + sheet "Registrar gestión" con promesa de pago.

## CXC-FE-PR5 — Cartera + reportes + anticipos (M) · `cxc-fe/pr5-cartera`
Antigüedad y estado de cuenta sobre `<ReporteShell>` (export PDF/Excel +
impresión) + lista de anticipos.

## CXC-FE-PR6 — Aplicación de pagos (M) · `cxc-fe/pr6-aplicaciones`
Bandeja + `<MatchingAplicacionTable>` (inline forms) + tolerancia no fiscal +
confirmar/rechazar de Ingresos.

## CXC-FE-PR7 — Alertas + hardening (S) · `cxc-fe/pr7-alertas-hardening`
Bandeja de alertas, indicadores del landing, accesibilidad, barrido de estados.

## Resumen de granularidad

| PR | Tamaño | Requiere backend |
|---|---|---|
| FE-PR1 | S | CXC-PR1 |
| FE-PR2 | M | CXC-PR1/PR-2 |
| FE-PR3 | M | CXC-PR4 |
| FE-PR4 | S | CXC-PR5 |
| FE-PR5 | M | CXC-PR3 + PR-6 |
| FE-PR6 | M | CXC-PR7 |
| FE-PR7 | S | CXC-PR8 |

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial |
