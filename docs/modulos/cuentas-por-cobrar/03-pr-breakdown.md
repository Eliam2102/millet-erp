# PR Breakdown — Módulo Cuentas por Cobrar (`Millet.CuentasPorCobrar`)

> **Versión:** v0.1 (borrador)
> **Fecha:** 2026-07-13
> **Depende de:** [`02-plan-implementacion.md`](02-plan-implementacion.md)

---

## 0. Cómo leer

Un PR a la vez, squash merge, trunk-based. Ramas `cxc/pr{N}-{slug}`. Cada PR
incluye sus tests, migrations revisadas como SQL, y actualización del doc del
módulo si cambia una decisión. DoD común: build + tests verdes, `what-if` si
toca `infra/`, Problem Details en errores nuevos, permisos aplicados en
endpoints.

## CXC-PR1 — Foundation (M) · rama `cxc/pr1-foundation`

- Proyecto `Millet.CuentasPorCobrar` (Domain/Application/Infrastructure) + referencia en `Millet.Api`.
- `CuentasPorCobrarDbContext`, esquema `cuentas_por_cobrar`, migration inicial.
- **Checklist DbContext nuevo (mismo PR):** `Program.cs` (`AddDbContext` + `MigrationsHealthCheckOptions.ContextTypes`) + `deploy-app-dev.yml`; app settings nuevos en `appservice.bicep`.
- Agregado `LineaCredito` (invariantes §4.2 del diseño) + endpoints `/lineas-credito` + seed inicial.
- Permisos canónicos `0000000a-*` en `PermisosCanonicos` + **migration en `IdentidadDbContext`**.
- Outbox wiring (`OutboxPublisherWorker<CuentasPorCobrarDbContext>`; el topic puede llegar en PR-4/PR-7 si aquí no se publica nada aún).

**DoD extra:** `/health/ready` verde en dev con el contexto nuevo.

## CXC-PR2 — Crédito disponible (S) · rama `cxc/pr2-credito-disponible`

- `CreditoDisponibleQuery` (`límite − facturado − liberado_sin_factura`) + endpoint.
- `facturado` = 0 provisional (hasta PR-3); `liberado_sin_factura` = 0 con `PLATFORM-TODO(<CreditoLiberadoSinFactura>)` (G1).
- Bandera `datoIncompleto=true` en la respuesta mientras cualquiera de los dos términos sea provisional.

## CXC-PR3 — Consumo de eventos de Facturación (L) · rama `cxc/pr3-eventos-facturacion`

- `FacturacionEventListenerWorker` (calco de los listeners de la triada) sobre `facturacion-events`, subscription `cuentas-por-cobrar-subscription` (**Bicep en el mismo PR**).
- Proyección `factura_cartera`: alta (`FacturaVentaTimbrada`), pagos (`ReciboPagoTimbrado`, `CobroMostradorRegistrado` ± cancelado), NC (`NotaCreditoTimbrada`), reversa (`ComprobanteCancelado`), anticipos (`FacturaAnticipoTimbrada`, solo estado de cuenta). Idempotencia por id del comprobante origen.
- Saldo neto 13-K derivado de la proyección.
- `IFacturacionAnticiposReadPort` en `CuentasPorCobrar/Domain/Ports/Facturacion/` + **cambio en Facturación**: promover `AnticipoSaldoDetalle` a contrato consumible + adapter. Endpoint `/anticipos?clienteId=`.
- Conecta el `facturado` real de PR-2 (se quita esa mitad de la bandera).

**DoD extra:** replays del mismo evento no duplican filas ni acumulados.

## CXC-PR4 — Decisión de liberación (M) · rama `cxc/pr4-liberacion`

- `DecisionLiberacion` (inmutable, snapshot de crédito) + `DecidirLiberacionCommand` con cascada serie → crédito → override.
- Catálogo seed `regla_liberacion_serie` (⚠️ gate suave: confirmar series nacionales con Prida).
- `AutorizacionCredito` consumible (calco `AutorizacionAperturaCaja`: un solo uso, ≤24 h, no autoconsumo) + endpoints.
- `DecisionLiberacionEmitidaEvent` vía outbox (topic `cuentas-por-cobrar-events` en Bicep si es el primer publicador).
- **Sin write-back a A+W** (eso es PR-9).

## CXC-PR5 — Seguimiento de cobranza (S) · rama `cxc/pr5-cobranza`

- `SeguimientoCobranza` append-only + `RegistrarSeguimientoCobranzaCommand` + bandeja por cliente.
- `promesa_pago` exige monto + fecha comprometida (check constraint).

## CXC-PR6 — Cartera / antigüedad / estado de cuenta (M) · rama `cxc/pr6-cartera`

- `AntiguedadSaldosQuery` con buckets configurables (parámetro del módulo) + clasificación A/B/C/E como atributo.
- `EstadoCuentaClienteQuery` (todos los movimientos aplicados; incluye anticipos vía read port).
- Contrato JSON ADR-0036 (`titulo/generadoEn/filtrosAplicados/columnas/filas/totales`).

## CXC-PR7 — Propuesta de aplicación de pago (M) · rama `cxc/pr7-aplicacion-pagos`

- `PropuestaAplicacionPago` + detalle por factura + matching desde remittance.
- Tolerancia no fiscal < $50 USD (parámetro) como `ajuste_no_fiscal`; invariante `Σ + ajuste = depósito`.
- `PropuestaAplicacionPagoCreadaEvent`; confirmación/rechazo manual interino de Ingresos (permiso `aplicacion-pago.confirmar`, A2).

## CXC-PR8 — Alertas de cartera (S) · rama `cxc/pr8-alertas`

- `AlertaCarteraWorker` (evaluación diaria): SOLUNION 90d, exceso de crédito, auto-bloqueo por vencimientos (bloquea la `LineaCredito` con motivo automático).
- `AlertaCarteraGeneradaEvent` + bandeja + atender. Notificaciones = `PLATFORM-TODO(<Notificaciones>)`.

## CXC-PR9 — Write-back de liberación a A+W (M) · rama `cxc/pr9-writeback-aw` · **BLOQUEADO**

- Worker: `DecisionLiberacionEmitidaEvent` → tabla-puente `MILLET_INTEGRACION` (contrato nuevo con equipo A+W; G1/G6/G-writeback).
- Script SQL on-prem con patrón stub+`ALTER` (SQL Server 2016 RTM) y **re-corrida manual en SER-DATA**.

## Resumen de granularidad

| PR | Tamaño | Bloqueo externo |
|---|---|---|
| PR-1 | M | — |
| PR-2 | S | — |
| PR-3 | L | toca Facturación (coordinar) |
| PR-4 | M | series nacionales (gate suave) |
| PR-5 | S | — |
| PR-6 | M | — |
| PR-7 | M | — |
| PR-8 | S | — |
| PR-9 | M | **contrato A+W** |

8 PRs de ruta normal + 1 dependiente. Total estimado: ~6-7 semanas de backend.

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial |
