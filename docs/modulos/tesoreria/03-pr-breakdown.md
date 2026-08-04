# PR Breakdown — Módulo Tesorería / Bancos (`Millet.Tesoreria`)

> **Versión:** 0.1 · **Fecha:** 2026-07-14
> **Basado en:** [`02-plan-implementacion.md`](02-plan-implementacion.md) v0.1.

---

## 0. Cómo leer

Ramas `tesoreria/pr{N}-{slug}` (prefijo de módulo, nunca `feature/`).
✅ `tesoreria/*` y `tesoreria-fe/*` están en la allowlist del hook
`validate-auto-merge` (alta 2026-07-14): auto-mode N2. Un PR por sesión,
squash merge, CI verde + rama al día con main.

---

## TES-PR1 — Foundation (M) · rama `tesoreria/pr1-foundation`

- Proyecto `Millet.Tesoreria` (Domain/Application/Infrastructure) calcado del exemplar Compras; alta en `Millet.sln`.
- `TesoreriaDbContext` + esquema `tesoreria` + primera migration (todas las tablas de [`01-diseno.md`](01-diseno.md) §5 **excepto** las de conciliación, que llegan con su PR).
- **Checklist DbContext nuevo en el mismo PR:** `Program.cs` (AddDbContext + `MigrationsHealthCheckOptions`) + `deploy-app-dev.yml`.
- Seeds: `concepto_movimiento` (catálogo inicial provisional) + `cuenta_bancaria` (placeholder hasta T-G2; marcar seed como iterable).
- Permisos canónicos `tesoreria.*` (§10 del diseño) + **migration en `IdentidadDbContext`**; GUIDs del siguiente bloque libre (validar).
- `HasCheckConstraint` en todos los enums persistidos del PR.
- **DoD:** deploy dev verde con `/health/ready` OK; permisos visibles en Admin.

## TES-PR2 — Movimientos bancarios (M) · rama `tesoreria/pr2-movimientos`

- Agregado `MovimientoBancario` + `AplicacionPagoProveedor` (invariantes §4.2; RN-3, RN-10, índice parcial RN-2).
- VOs `Clabe`/`NumeroCuenta` con masking (enricher Serilog ya existente para CLABE).
- `RegistrarMovimientoIngresoCommand`, queries de libro y saldos, endpoints GET.
- Stub `IPeriodoContablePort` siempre-abierto con `PLATFORM-TODO(<PeriodoContableCerrado>)`.
- **DoD:** alta de ingreso manual funcional; egresos aún no (llegan con PR-4).

## TES-PR3 — Bandeja de pasivos (M) · rama `tesoreria/pr3-bandeja-pasivos`

- `CuentasPorPagarEventListenerWorker` (en Tesorería): subscription nueva en `cuentas-por-pagar-events` (Bicep + filtro SQL por EventType, `what-if` primero) → upsert `pasivo_pendiente_pago` con dedupe `evento_procesado`.
- `IProveedorBancoReadPort` **nuevo en DatosMaestros** (CLABE/banco del proveedor) + adapter; masking en DTO [T-G1].
- `BandejaPasivosPendientesQuery` con filtros.
- **Decidir e implementar backfill** de pasivos ya `Autorizada` en CxP previos a la subscription ([`04-cuidados-infra.md`](04-cuidados-infra.md) §3.1).
- **DoD:** pasivo autorizado en dev aparece en la bandeja con datos bancarios resueltos.

## TES-PR4 — Pago a proveedor end-to-end (L) · rama `tesoreria/pr4-pago-proveedor` — **contrato congelado**

- Outbox del módulo + `OutboxPublisherWorker<TesoreriaDbContext>`.
- Publisher de los 4 eventos `tesoreria.*.v1` — payloads espejo **exactos**; incluir test de contrato contra los records de `ContratosEspejo.cs` de CxP.
- `RegistrarPagoProveedorCommand` (multi-pasivo, `aplicado.v1` por factura — RN-4), `RevertirPagoProveedorCommand` (contramovimiento), `SolicitarCancelacionPasivoCommand`.
- Actualización de `saldo_pendiente` local en la proyección.
- **DoD:** ciclo completo en dev: autorizar factura en CxP → bandeja → pagar → CxP pasa a `Pagada`; revertir → CxP regresa a `Autorizada`.

## TES-PR5 — Corrida de pagos + oficio (M) · rama `tesoreria/pr5-corridas` — gate T-G4

- `CorridaPago` + máquina de estados (RN-5) + comandos §7.1.
- Integración con matriz de autorización (o stub `<MatrizAutorizacionCompartida>` si no hay contrato público — verificar primero).
- `OficioCarteraQuery` (JSON ADR-0036); ejecución por línea reutiliza el flujo de PR-4.
- **DoD:** corrida Borrador→Autorizada→Ejecutada con eventos por línea.

## TES-PR6 — Pago a cuenta (S) · rama `tesoreria/pr6-pago-a-cuenta`

- `RegistrarPagoACuentaCommand` (gate RN-2 + autorización), `LigarPagoACuentaCommand` (liga tardía sin re-desembolso → `aplicado.v1`).
- `PagosACuentaAbiertosQuery` con antigüedad; sugerencia de liga al recibir pasivo del mismo proveedor (en el listener de PR-3).
- **DoD:** segundo pago a cuenta al mismo proveedor bloqueado; liga tardía cierra el abierto y paga el pasivo.

## TES-PR7 — Ingresos (M) · rama `tesoreria/pr7-ingresos` — **PR gemelo en Facturación**

- Listeners: `cuentas-por-cobrar-events` (propuestas) y `facturacion-events` (caja-sesion.cerrada + recibo-pago.timbrado); subscriptions nuevas en Bicep.
- `DepositoConfirmacion` + `ConfirmarDepositoCommand` (RN-6, publica `pago-cliente.confirmado.v1`) + `RechazarPropuestaDepositoCommand` (publica `propuesta-aplicacion.rechazada.v1` [T-G7]).
- **PR gemelo (Facturación):** listener de `tesoreria-events` que invoca `EmitirReppCommand` — cierra `<PagoClienteConfirmado>`; coordinar payload final del evento con ese PR.
- **DoD:** propuesta CxC → confirmación → REPP timbrado → CxC aplica a cartera → depósito marcado `repp_timbrado`.

## TES-PR8 — REPP recibido de proveedor (S) · rama `tesoreria/pr8-repp-recibido` — gate T-G11

- `ReppProveedorRecibido` + `RegistrarReppRecibidoCommand` (XML a Blob ADR-0024) → `repp-proveedor.recibido.v1` → CxP libera `FALTA_REPP`.
- `ReppPendientesQuery` (SLA 5 días); si T-G11 no está resuelto, versión degradada sin filtro PPD + `PLATFORM-TODO(<MetodoPagoEnPasivo>)`.
- **DoD:** registro de REPP libera el motivo de revisión en CxP (verificar en dev).

## TES-PR9 — Conciliación bancaria (L) · rama `tesoreria/pr9-conciliacion` — gate T-G2

- Tablas `conciliacion`/`extracto_linea` (migration), `IExtractoFuente` + `ArchivoExtractoAdapter` con perfiles por banco.
- Motor de matching (referencia > monto+fecha > tolerancia por cuenta), confirmación individual/lote, alta asistida, cierre RN-7 + `ActaConciliacionQuery`.
- Saldos iniciales por cuenta a fecha de corte [T-G8].
- **DoD:** extracto real de un banco de Millet concilia un mes en dev.

## TES-PR10 — Reportes (S) · rama `tesoreria/pr10-reportes`

- `FlujoEfectivoReporteQuery` (clasificación por concepto) + `AuxiliarBancosReporteQuery`, formato JSON ADR-0036.
- **DoD:** ambos endpoints devuelven el contrato `{ titulo, generadoEn, filtrosAplicados, columnas, filas, totales }`.

## Dependientes

- **TES-PR11 (S)** · `tesoreria/pr11-contabilizacion` — `tesoreria.movimiento-bancario.registrado.v1` al outbox para Contabilidad futura [T-G5]; diseño de payload contable en su momento.
- **TES-PR12** · fuera del módulo (`Millet.Integraciones.<Banco>`) — conector vivo tras `IExtractoFuente`; requiere decisión de banco/agregador.

---

## Resumen de granularidad

| PR | Tamaño | Gate |
|---|---|---|
| PR1 foundation | M | — |
| PR2 movimientos | M | — |
| PR3 bandeja | M | backfill (decisión) |
| PR4 pago e2e | L | — |
| PR5 corridas | M | T-G4 |
| PR6 pago a cuenta | S | — |
| PR7 ingresos | M | PR gemelo Facturación |
| PR8 REPP | S | T-G11 |
| PR9 conciliación | L | T-G2, T-G8 |
| PR10 reportes | S | — |

Ruta crítica: PR1→PR2→PR3→PR4. Tras PR4: PR5/PR6/PR7/PR8/PR10 paralelizables; PR9 tras PR2.

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-14 | Primera versión. |
