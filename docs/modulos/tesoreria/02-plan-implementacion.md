# Plan de implementación — Módulo Tesorería / Bancos (`Millet.Tesoreria`)

> **Versión:** 0.1 · **Fecha:** 2026-07-14
> **Basado en:** [`00-levantamiento.md`](00-levantamiento.md) v0.1 y [`01-diseno.md`](01-diseno.md) v0.1.

---

## 0. Cómo leer

Fases ordenadas por dependencia; cada fase = 1 PR backend (tamaños
S/M/L, criterio de PRs consolidados del proyecto). La ruta crítica es
F0→F3 (cierra el contrato congelado con CxP). Las fases 4-7 son
paralelizables entre sí después de F1. El detalle por PR vive en
[`03-pr-breakdown.md`](03-pr-breakdown.md).

---

## 1. Resumen ejecutivo

- **10 PRs backend** (8 core + 2 dependientes) + **1 PR gemelo en
  Facturación** (listener de `pago-cliente.confirmado`).
- El módulo es principalmente **integración y libro mayor bancario**: la
  complejidad no está en cálculos sino en contratos (payloads espejo
  exactos) y en la máquina de estados de corrida/conciliación.
- Dos gates funcionales antes de fases tardías: T-G4 (matriz de corridas)
  bloquea F4; T-G2 (formatos de extracto) bloquea F6. Nada bloquea F0-F3.

## 2. Prerrequisitos — audit del repo (2026-07-14)

| Prerrequisito | Estado |
|---|---|
| Topic `tesoreria-events` en Bicep | ✅ existe (`servicebus.bicep`) |
| Consumer de CxP (`TesoreriaEventListenerWorker` + subscription) | ✅ desplegado (F9-PR1) |
| Payloads espejo (`ContratosEspejo.cs:97-141`) | ✅ congelados |
| Publisher de `pasivo.autorizado-para-pago.v1` en CxP | ✅ desplegado |
| Publisher de `propuesta-aplicacion.creada.v1` en CxC | ✅ mergeado (CXC-PR7) |
| Publishers de `caja-sesion.cerrada` / `recibo-pago.timbrado` | ✅ Facturación |
| Matriz de autorización reutilizable expuesta como contrato público | ⚠️ verificar en F4 (posible `PLATFORM-TODO(<MatrizAutorizacionCompartida>)`) |
| Read-port de datos bancarios de proveedor en DatosMaestros | ❌ no existe — se crea en F2 (T-G1) |
| Backfill de pasivos autorizados pre-subscription | ⚠️ decidir en F2 (ver [`04-cuidados-infra.md`](04-cuidados-infra.md) §3) |

## 3. Fases

### Fase 0 — Foundation del módulo (PR-1 · M)
Esquema `tesoreria` + `TesoreriaDbContext` (checklist ADR-0030 completo),
`CuentaBancaria` + `ConceptoMovimiento` con seeds, permisos canónicos
`tesoreria.*` + migration de Identidad, registro del módulo. Sin lógica
de negocio.

### Fase 1 — Libro de movimientos (PR-2 · M)
Agregado `MovimientoBancario` + `AplicacionPagoProveedor`, invariantes
RN-3/RN-10, índice parcial RN-2, endpoints de consulta y alta de ingreso
manual, `SaldosPorCuentaQuery`.

### Fase 2 — Bandeja de pasivos (PR-3 · M)
Listener de `pasivo.autorizado-para-pago.v1` → proyección
`pasivo_pendiente_pago` (idempotente) + `IProveedorBancoReadPort` nuevo
en DatosMaestros con masking [T-G1] + decisión de backfill.

### Fase 3 — Pago a proveedor end-to-end (PR-4 · L) — **cierra el contrato congelado**
Outbox del módulo + publisher de los 4 eventos `tesoreria.*.v1` con
payloads espejo exactos; `RegistrarPagoProveedorCommand` (bandeja →
movimiento → aplicaciones → `aplicado.v1` por factura), reversa con
contramovimiento, `SolicitarCancelacionPasivoCommand`. Al mergear, el
worker de CxP empieza a recibir tráfico real y
`<TesoreriaEventListenerCompras>` queda desbloqueado (wiring de Compras,
fuera de alcance).

### Fase 4 — Corrida de pagos (PR-5 · M) — gate T-G4
Máquina de estados, matriz de autorización (o stub), oficio de cartera
(`<ReporteShell>` — el JSON del backend en este PR; el PDF es de FE).

### Fase 5 — Pago a cuenta (PR-6 · S)
Gate RN-2, `LigarPagoACuentaCommand` (reconciliación tardía), read model
de abiertos con antigüedad.

### Fase 6 — Ingresos (PR-7 · M) — **requiere PR gemelo en Facturación**
Listeners de CxC y Facturación, `DepositoConfirmacion`, publisher de
`pago-cliente.confirmado.v1` y `propuesta-aplicacion.rechazada.v1` [T-G7].
Coordinar con Facturación el listener que invoca `EmitirReppCommand`
(cierra `<PagoClienteConfirmado>` y `<TesoreriaCajaSesion>`).

### Fase 7 — REPP recibido (PR-8 · S) — gate T-G11
`ReppProveedorRecibido` + publisher `repp-proveedor.recibido.v1` + read
model de pendientes (requiere `MetodoPago`: resolver T-G11 antes o
degradar el read model a "pagos sin REPP" sin filtro PPD).

### Fase 8 — Conciliación (PR-9 · L) — gate T-G2
`Conciliacion`/`ExtractoLinea`, `IExtractoFuente` + `ArchivoExtractoAdapter`
con perfiles, motor de matching, alta asistida, cierre + acta. Arranque
con saldos iniciales [T-G8].

### Fase 9 — Reportes (PR-10 · S)
`FlujoEfectivoReporteQuery` + `AuxiliarBancosReporteQuery` (JSON ADR-0036).

### Fases dependientes
- **PR-11 (S)** — eventos de contabilización al outbox [T-G5].
- **PR-12 (fuera del módulo)** — `Millet.Integraciones.<Banco>` tras
  `IExtractoFuente`.

## 4. Cronograma sugerido (1 dev backend + 1 dev frontend)

| Semana | Backend | Frontend |
|---|---|---|
| 1 | PR-1 → PR-2 | — |
| 2 | PR-3 → PR-4 | FE-PR1 (foundation) |
| 3 | PR-5 → PR-6 | FE-PR2 (movimientos + pago) |
| 4 | PR-7 (+ gemelo Facturación) | FE-PR3 (corridas) |
| 5 | PR-8 → PR-10 | FE-PR4 (ingresos) |
| 6 | PR-9 | FE-PR5 (conciliación) |
| 7 | buffer / PR-11 | FE-PR6 (REPP + reportes + hardening) |

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| PR gemelo de Facturación se atrasa | El endpoint manual de REPP sigue operando; PR-7 se mergea igual (el evento queda publicado sin consumidor, como otros del repo) |
| T-G2/T-G4 sin respuesta de Javier | Reordenar: PR-6/PR-8/PR-10 antes que PR-5/PR-9 |
| Matriz de Compras sin contrato público | Stub local con `PLATFORM-TODO(<MatrizAutorizacionCompartida>)`; no bloquea |
| Payload espejo divergente (typo en nombres de campo) | Test de contrato en PR-4 que serializa el evento y lo deserializa con los records espejo de CxP (`ContratosEspejo.cs`) |
| Backfill de pasivos pre-subscription olvidado | Cuidado P0 en [`04-cuidados-infra.md`](04-cuidados-infra.md) §3; decidir en PR-3 |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
