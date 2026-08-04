# Plan de implementación de frontend — Módulo Cuentas por Cobrar

> **Versión:** v0.1 (borrador)
> **Fecha:** 2026-07-13
> **Depende de:** [`05-frontend-diseno.md`](05-frontend-diseno.md) · backend según [`02-plan-implementacion.md`](02-plan-implementacion.md)

---

## 0. Cómo leer

Fases FE-0..FE-6, ramas `cxc-fe/*`. Cada fase arranca cuando su contraparte de
backend está mergeada (columna "Requiere"). Un dev frontend en paralelo al
backend, con una fase de desfase.

## 1. Resumen ejecutivo

El módulo es mayormente bandejas + reportes sobre patrones ya existentes; las
dos pantallas con diseño propio son el **matching de aplicación de pago** y el
**flujo de decisión de liberación**. Riesgo bajo; el grueso es replicación
disciplinada del exemplar.

## 2. Prerrequisitos

- Nav shell de cards operando (patrón ya definido cross-módulo).
- `<ReporteShell>` y export utils disponibles (ya en uso por otros módulos).
- Permisos `cuentas_por_cobrar.*` seedeados (CXC-PR1).

## 3. Fases

### FE-0 — Foundation (S) · requiere CXC-PR1
Card en nav shell, ruta `/cxc` (landing), guards de permisos, tipos y client de
API generados/escritos para líneas de crédito.

### FE-1 — Líneas de crédito (M) · requiere CXC-PR1 (+PR-2 para el disponible)
`/cxc/lineas-credito` (P1) + detalle (P3) con `<CreditoDisponibleCard>` y banner
`datoIncompleto`; sheet "Nueva línea"; bloquear/desbloquear con motivo.

### FE-2 — Liberaciones (M) · requiere CXC-PR4
`/cxc/liberaciones` (P2), flujo de decisión (panel de acción), gestión de
autorizaciones consumibles (crear/cancelar/consumir), badges de resultado.

### FE-3 — Cobranza (S) · requiere CXC-PR5
`/cxc/cobranza` con `<TimelineCobranza>` + sheet "Registrar gestión" (canal,
resultado, promesa con monto/fecha).

### FE-4 — Cartera y reportes (M) · requiere CXC-PR3 + CXC-PR6
`/cxc/cartera` (antigüedad con buckets) y `/cxc/estado-cuenta` sobre
`<ReporteShell>`; `/cxc/anticipos` (lista filtrable del read port). Impresión
limpia (`data-print="hidden"`).

### FE-5 — Aplicación de pagos (M) · requiere CXC-PR7
`/cxc/aplicaciones` (P1+P3) + `<MatchingAplicacionTable>` con inline forms,
tolerancia no fiscal, confirmación/rechazo (permiso de Ingresos).

### FE-6 — Alertas + hardening (S) · requiere CXC-PR8
`/cxc/alertas`, indicadores del landing, pase de accesibilidad y estados
vacíos/error en todas las rutas.

## 4. Cronograma (1 dev frontend)

| Semana | Fase |
|---|---|
| 2 | FE-0 |
| 3 | FE-1 |
| 4 | FE-2 |
| 5 | FE-3 |
| 6 | FE-4 |
| 7 | FE-5 + FE-6 |

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| Matching de aplicación es la única pantalla sin precedente directo | Prototipar primero con datos fixture; validar con el Encargado antes de conectar API |
| CI no corre vitest FE (deuda conocida) | Correr vitest local antes de cada merge; no confiar en el gate |
| Multi-moneda mal renderizada (mezclar MXN/USD en totales) | Nunca sumar montos de distinta divisa en UI; totales por moneda |

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial |
