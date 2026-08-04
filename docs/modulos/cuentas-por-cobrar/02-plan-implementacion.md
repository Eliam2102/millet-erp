# Plan de implementación — Módulo Cuentas por Cobrar (`Millet.CuentasPorCobrar`)

> **Versión:** v0.1 (borrador)
> **Fecha:** 2026-07-13
> **Depende de:** [`00-levantamiento.md`](00-levantamiento.md) §6 (secuencia de PRs v0.2) · [`01-diseno.md`](01-diseno.md)

---

## 0. Cómo leer

Las fases agrupan los 9 PRs del levantamiento §6 (numeración `CXC-PR1..PR9`, se
conserva para no romper referencias). Tamaños S/M/L según la convención de PRs
consolidados (S-M, no microscópicos). El frontend se planifica en
[`06-frontend-plan-implementacion.md`](06-frontend-plan-implementacion.md).

## 1. Resumen ejecutivo

- **Ruta crítica:** PR-1 → PR-3 → PR-6 (fundaciones → proyección de cartera →
  reportes). Todo lo demás cuelga de PR-1 y puede intercalarse.
- **Dos puntos de sincronización externa:** PR-3 toca Facturación (promover
  `AnticipoSaldoDetalle` a puerto público) y PR-9 está bloqueado por contrato
  con el equipo A+W. Ninguno bloquea el arranque.
- **El MVP entrega decisión y cálculo, no efecto sobre A+W** (CXC-2): la
  liberación se persiste y audita en el ERP; el write-back llega en PR-9.

## 2. Prerrequisitos — audit del repo (2026-07-13)

| Prerrequisito | Estado |
|---|---|
| Eventos de Facturación publicándose (`facturacion-events`) | ✅ `FacturacionIntegrationEvents.cs` (7 eventos relevantes) |
| Motor de reportes | ✅ ADR-0036 cerrado, `<ReporteShell>` en uso (CxP/Almacén) |
| Master `Cliente` | ✅ `DatosMaestros.Domain.Cliente` (ADR-0048 D6) |
| Patrón autorización consumible | ✅ `AutorizacionAperturaCaja` (calco directo) |
| Patrón listener worker | ✅ 5 listeners de la triada como referencia |
| `AnticipoSaldoDetalle` como contrato público | ❌ es record interno de `Facturacion.Application` → se resuelve en PR-3 |
| Emisor `PagoClienteConfirmadoEvent` | ❌ stub (`PLATFORM-TODO(<PagoClienteConfirmado>)`) → interino manual (A2) |
| Vista A+W "liberado sin factura" (G1) | ❌ → `PLATFORM-TODO(<CreditoLiberadoSinFactura>)` |
| Levantamiento v0.3 (pendientes de owners) | ⏳ no bloquea PR-1–PR-3; sí conviene antes de PR-4 (series de folio) |

## 3. Fases

### Fase 0 — Foundation del módulo (PR-1 · M)
Proyecto `Millet.CuentasPorCobrar` (Domain/Application/Infrastructure),
`CuentasPorCobrarDbContext` (esquema `cuentas_por_cobrar`) + checklist DbContext
nuevo (`Program.cs` + `deploy-app-dev.yml` en el mismo PR), `LineaCredito`
completa (agregado, migration, endpoints CRUD + bloquear/desbloquear, seed),
permisos canónicos `0000000a-*` + migration en `IdentidadDbContext`, outbox
wiring.

### Fase 1 — Crédito y cartera core (PR-2 + PR-3 · S + L)
- **PR-2 (S):** `CreditoDisponibleQuery` con `facturado` provisional en 0 y
  bandera `dato_incompleto`; término `liberado_sin_factura` como
  `PLATFORM-TODO(<CreditoLiberadoSinFactura>)`.
- **PR-3 (L):** `FacturacionEventListenerWorker` + subscription
  `cuentas-por-cobrar-subscription` (Bicep), proyección `factura_cartera`
  (7 eventos, idempotente), saldo neto 13-K, `IFacturacionAnticiposReadPort`
  (incluye el cambio en Facturación). Al mergear, PR-2 deja de estar en 0.

### Fase 2 — Liberación de pedidos (PR-4 · M)
`DecisionLiberacion` + `ReglaLiberacionSerie` (seed) + `AutorizacionCredito`
(calco consumible) + `DecidirLiberacionCommand` + `DecisionLiberacionEmitidaEvent`.
**Gate suave:** confirmar con Prida las series nacionales antes de sembrar el
catálogo; si no llega, se siembra el internacional y se ajusta por seed posterior.

### Fase 3 — Cobranza (PR-5 · S)
`SeguimientoCobranza` append-only + bandeja por cliente.

### Fase 4 — Reportes de cartera (PR-6 · M)
Antigüedad de saldos (buckets configurables) + estado de cuenta + endpoints
ADR-0036 sobre `factura_cartera`.

### Fase 5 — Aplicación de pagos (PR-7 · M)
`PropuestaAplicacionPago` + matching desde remittance + tolerancia no fiscal +
`PropuestaAplicacionPagoCreadaEvent` + confirmación manual interina (A2) +
topic `cuentas-por-cobrar-events` en Bicep (si no salió antes con PR-4).

### Fase 6 — Alertas (PR-8 · S)
`AlertaCarteraWorker` (SOLUNION 90d, exceso, auto-bloqueo) + bandeja + atender.

### Fase dependiente — Write-back A+W (PR-9 · M)
Worker `DecisionLiberacionEmitidaEvent` → tabla-puente `MILLET_INTEGRACION`.
**Bloqueado por contrato A+W** (G1/G6/G-writeback). Recordar: cambios a
`03_create_views.sql` se re-corren a mano en SER-DATA (SQL Server 2016 RTM).

## 4. Cronograma sugerido (1 dev backend + 1 dev frontend)

| Semana | Backend | Frontend |
|---|---|---|
| 1 | PR-1 | — |
| 2 | PR-2 + arranque PR-3 | FE-0 (foundation) |
| 3 | PR-3 | FE-1 (líneas de crédito) |
| 4 | PR-4 | FE-2 (liberaciones) |
| 5 | PR-5 + PR-6 | FE-3 (cobranza) |
| 6 | PR-7 | FE-4 (cartera/reportes) |
| 7 | PR-8 + buffer | FE-5 (aplicación pagos) + FE-6 |
| — | PR-9 cuando A+W cierre contrato | — |

## 5. Riesgos del plan

| Riesgo | Mitigación |
|---|---|
| A+W no cierra G1 → crédito disponible inexacto | Bandera `dato_incompleto` visible en UI; el módulo opera igual |
| Series de folio nacionales distintas a las internacionales | Catálogo seed versionado; corregir filas no requiere código |
| Emisor de `PagoClienteConfirmadoEvent` nunca llega en el MVP | Confirmación manual de Ingresos con permiso propio (A2) es funcionalmente completa |
| Cambio en Facturación (PR-3) choca con trabajo en curso de ese módulo | Cambio quirúrgico (promover un record + adapter); coordinar rama y sesión |
| Migración histórica SAP indefinida | Fuera de la ruta crítica; extractor one-shot se planifica cuando Prida confirme alcance |

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial |
