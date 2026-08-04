# Diseño — Módulo Cuentas por Cobrar (`Millet.CuentasPorCobrar`)

> **Versión:** v0.1 (borrador — deriva del levantamiento v0.2)
> **Fecha:** 2026-07-13
> **Depende de:** [`00-levantamiento.md`](00-levantamiento.md) (mapa funcional + decisiones CXC-1..7 + validación contra código §9)
> **Estado:** borrador; se cierra cuando el levantamiento pase a v0.3 (pendientes Prida / fiscal / A+W)

---

## 0. Cómo leer este documento

Este documento traduce el mapa funcional a decisiones técnicas: dominio, esquema,
puertos, eventos, workers, RBAC y endpoints. Sigue el formato del exemplar de CxP
([`../cuentas-por-pagar/01-diseno.md`](../cuentas-por-pagar/01-diseno.md)). Todo
lo funcional (AS-IS/TO-BE, reglas de negocio, gaps) vive en el levantamiento y no
se repite aquí.

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

Módulo 4 del back-office. Es el espejo de CxP del lado del ingreso: donde CxP
gestiona el ciclo del pasivo, CxC gestiona el **ciclo del cobrable**. Frontera
dura con Facturación: Facturación emite CFDI (ingreso, FANT, egreso/NC, REPP);
CxC nunca timbra — consume los eventos de timbrado y opera cartera, crédito,
cobranza y aplicación de pagos.

### 1.2 Alcance funcional v1 (MVP)

**Dentro:** líneas de crédito (entidad propia, seed inicial), crédito disponible,
decisión de liberación de pedidos (con reglas por serie + override consumible),
seguimiento de cobranza auditable, proyección de cartera desde eventos,
antigüedad de saldos + estado de cuenta (ADR-0036), propuesta de aplicación de
pago, saldo neto 13-K, alertas de cartera (SOLUNION 90d, exceso, auto-bloqueo),
consumo de `AnticipoSaldoDetalle`.

**Fuera:** emisión de cualquier CFDI (Facturación), ejecución de pagos/REPP
(Facturación + Ingresos), integración automatizada con portal SOLUNION (CXC-6),
write-back de liberación a A+W (PR-9 dependiente), migración de cartera
histórica SAP (one-shot ADR-0044, se planifica aparte), préstamos/cobranza
judicial.

### 1.3 Volúmenes esperados

Bajos en términos de sistema: decenas de facturas/día, 2 cortes de depósitos/día,
cartera de cientos de clientes activos. Ninguna decisión de diseño necesita
optimizarse por volumen; se optimiza por **auditabilidad** (cada decisión de
crédito y cada gestión de cobranza deja rastro).

---

## 2. Decisiones de diseño y ADRs aplicados

| Decisión / ADR | Aplicación en CxC |
|---|---|
| ADR-0007 (RBAC granular) | Permisos canónicos `cuentas_por_cobrar.*`, namespace GUID `0000000a-*` (§10) |
| ADR-0009 (Outbox) | `OutboxPublisherWorker<CuentasPorCobrarDbContext>` → topic `cuentas-por-cobrar-events` |
| ADR-0010 (Problem Details) | Errores de negocio vía `BusinessRuleException` → RFC 7807 |
| ADR-0012 (ETag/If-Match) | `version` en `LineaCredito`, `PropuestaAplicacionPago` |
| ADR-0020 (Idempotency-Key) | Todas las mutaciones HTTP |
| ADR-0021 (Versionado) | `/api/v1/cuentas-por-cobrar/...` |
| ADR-0030 (schema-per-module) | Esquema `cuentas_por_cobrar`, `CuentasPorCobrarDbContext` propio |
| ADR-0031 (PLATFORM-TODO) | §13 Dependencias de plataforma pendientes |
| ADR-0036 (Reportería) | Antigüedad + estado de cuenta = JSON estructurado + `<ReporteShell>` |
| ADR-0041 (lectura ≠ mutación) | `*.leer` separado de `*.gestionar`/`*.decidir` |
| ADR-0044 (SAP one-shot) | Cero vistas contra SAP; migración histórica como extractor único |
| ADR-0048 (tabla-puente) | Write-back de liberación (PR-9) vía `MILLET_INTEGRACION` |
| CXC-1..CXC-7 | Ver levantamiento §7 |
| `[Decisión 12-1]` de Caja | Override de crédito = autorización consumible, calco de `AutorizacionAperturaCaja` |

---

## 3. Asunciones que deben confirmarse

Heredadas del levantamiento (§8): series de folio nacionales y plazos de crédito
(Prida), tolerancia < $50 USD sin CFDI (fiscal), G1/G6/G-writeback (A+W), alcance
de la migración histórica (Prida). Además, propias del diseño:

- **A1.** La notificación de depósitos bancarios (2×/día) se captura manualmente
  en el MVP (no hay feed bancario automatizado). `PLATFORM-TODO(<DepositoBancarioFeed>)`.
- **A2.** El emisor de `PagoClienteConfirmadoEvent` publica en `tesoreria-events`
  (topic ya existente); hasta entonces la propuesta se marca confirmada por
  endpoint manual de Ingresos con permiso propio.
- **A3.** `Cliente` de `DatosMaestros` (ADR-0048 D6) es el master de clientes que
  CxC referencia; no se crea catálogo paralelo.

---

## 4. Modelo del dominio

### 4.1 Agregados raíz

| Agregado | Propósito | Estados |
|---|---|---|
| `LineaCredito` | Límite/moneda/origen/plazo por cliente | `Activa` / `Bloqueada` / `Suspendida` |
| `DecisionLiberacion` | Decisión auditada de crédito sobre un pedido A+W | `Liberado` / `Retenido` / `LiberadoConOverride` (inmutable post-decisión) |
| `PropuestaAplicacionPago` | Matching depósito↔facturas con detalle por factura | `Propuesta` / `Confirmada` / `Rechazada` |
| `AutorizacionCredito` | Override consumible (calco `AutorizacionAperturaCaja`) | `Autorizada` / `Usada` / `Cancelada` |
| `FacturaCartera` | **Proyección** local de comprobantes cobrables (no se muta por comandos, solo por eventos) | `Abierta` / `Parcial` / `Pagada` / `Cancelada` |

Entidades no-agregado: `SeguimientoCobranza` (append-only por cliente),
`AlertaCartera` (generada por worker), `ReglaLiberacionSerie` (catálogo seed).

### 4.2 Invariantes principales

- `LineaCredito`: una activa por (`cliente_id`, `moneda`); `limite > 0`;
  bloquear exige `motivo_bloqueo`.
- `DecisionLiberacion`: inmutable una vez emitida (correcciones = nueva decisión);
  `LiberadoConOverride` exige `override_id` consumido en la misma transacción;
  guarda `credito_disponible_snapshot` siempre.
- `AutorizacionCredito`: un solo uso; vigencia ≤ 24 h; supervisor ≠ beneficiario;
  motivo obligatorio ≤ 254 (mismas invariantes que `AutorizacionAperturaCaja`).
- `PropuestaAplicacionPago`: `Σ importe_aplicado + ajuste_no_fiscal = monto_deposito`;
  `ajuste_no_fiscal` solo negativo y `< tolerancia` (parámetro, default $50 USD);
  sin `remittance_ref` no se puede proponer (regla 2.1 del levantamiento).
- `FacturaCartera`: `monto_pagado + monto_nc ≤ total`; transiciones solo por
  eventos idempotentes (correlación por id de evento origen).
- `SeguimientoCobranza`: append-only; `promesa_pago` exige monto + fecha comprometida.

### 4.3 Value objects

`Dinero(monto, moneda)` (reuso del SharedKernel si existe; no convertir monedas),
`BucketAntiguedad` (rango de días, configurable), `RemittanceRef`.

---

## 5. Esquema PostgreSQL

Tablas y columnas: levantamiento §3.1–3.2 (fuente de verdad). Complementos de
implementación:

### 5.1 Índices críticos

- `linea_credito (cliente_id, moneda) UNIQUE WHERE estado = 'Activa'`
- `factura_cartera (factura_venta_id) UNIQUE` — idempotencia de proyección
- `factura_cartera (cliente_id, estado)` — cartera y antigüedad
- `factura_cartera (fecha_vencimiento) WHERE estado IN ('Abierta','Parcial')` — alertas
- `decision_liberacion (pedido_ref)` · `seguimiento_cobranza (cliente_id, fecha DESC)`
- `propuesta_aplicacion_pago (estado) WHERE estado = 'Propuesta'` — bandeja

### 5.2 Constraints

Todo enum persistido con `HasCheckConstraint` + mirror FE (incidente 2026-07-11):
`estado` de los 5 agregados, `resultado`, `regla_aplicada`, `canal`,
`comportamiento`, `tipo` de alerta.

### 5.3 Migraciones

`CuentasPorCobrarDbContext` nuevo → checklist obligatorio en el mismo PR:
`Program.cs` (`AddDbContext` + `MigrationsHealthCheckOptions.ContextTypes`) y
`deploy-app-dev.yml`. Los permisos canónicos generan además migration en
`IdentidadDbContext` (HasData).

---

## 6. Puertos y adaptadores

### 6.1 Puertos que CxC consume (define en `CuentasPorCobrar/Domain/Ports/`)

| Puerto | Implementa | Uso |
|---|---|---|
| `IClienteReadPort` | DatosMaestros | Nombre/RFC/datos del cliente para bandejas y reportes |
| `IFacturacionAnticiposReadPort` | Facturación (**nuevo**, promueve `AnticipoSaldoDetalle` desde `Facturacion.Application`) | Lista de saldos de anticipo por cliente (§1.4 levantamiento) |
| `IUsuarioReadPort` | Identidad | Nombres en auditoría (calco del de Almacén) |
| `ISucursalReadPort` / `ITipoCambioReadPort` | Administración/Compartido | Solo si el reporte lo exige; evitar consumos prematuros |

### 6.2 Puertos que CxC expone

Ninguno en v1. Candidato futuro: `ICxcCreditoReadPort` (¿cliente bloqueado?,
¿crédito disponible?) para Facturación/Caja; se difiere hasta que exista el
consumidor.

### 6.3 Adaptadores

Adapters de lectura en `Infrastructure/PublicAdapters` del módulo dueño (patrón
existente: `UsuarioServicioReadAdapter` en Identidad). El adapter de
`IFacturacionAnticiposReadPort` vive en Facturación y reusa el handler de
`FacturaAnticipoDetalleQuery`.

---

## 7. Comandos y queries (CQRS, MediatR)

### 7.1 Comandos

| Comando | Regla clave |
|---|---|
| `CrearLineaCreditoCommand` / `EditarLineaCreditoCommand` | única activa por cliente+moneda; ETag |
| `BloquearLineaCreditoCommand` / `DesbloquearLineaCreditoCommand` | motivo obligatorio |
| `DecidirLiberacionCommand` | evalúa serie → crédito → override; persiste snapshot; emite `DecisionLiberacionEmitidaEvent` |
| `CrearAutorizacionCreditoCommand` / `CancelarAutorizacionCreditoCommand` | permiso `liberacion.override`; vigencia |
| `RegistrarSeguimientoCobranzaCommand` | append-only |
| `CrearPropuestaAplicacionPagoCommand` | matching desde remittance; tolerancia no fiscal; emite `PropuestaAplicacionPagoCreadaEvent` |
| `ConfirmarPropuestaAplicacionPagoCommand` *(interino A2)* | endpoint manual de Ingresos hasta que exista el emisor real |
| `RechazarPropuestaAplicacionPagoCommand` | motivo |
| `MarcarAlertaAtendidaCommand` | |

### 7.2 Queries

Bandejas paginadas server-side (patrón P1/P2): líneas de crédito, decisiones de
liberación, propuestas, seguimientos por cliente, alertas. Reportes ADR-0036:
`AntiguedadSaldosQuery`, `EstadoCuentaClienteQuery`, `SaldoNetoClienteQuery`
(13-K) — devuelven `{ titulo, generadoEn, filtrosAplicados, columnas, filas,
totales }`. `CreditoDisponibleQuery` devuelve además `dato_incompleto` mientras
G1 siga abierto.

---

## 8. Eventos de integración

### 8.1 Publicados (topic `cuentas-por-cobrar-events`)

Naming `{Agregado}{Verbo}Event`, tipo `cuentas_por_cobrar.{recurso}.{accion}.v1`:

| Evento | Tipo | Consumidor |
|---|---|---|
| `DecisionLiberacionEmitidaEvent` | `cuentas_por_cobrar.decision-liberacion.emitida.v1` | Write-back worker (PR-9); Notificaciones |
| `PropuestaAplicacionPagoCreadaEvent` | `cuentas_por_cobrar.propuesta-aplicacion.creada.v1` | Ingresos |
| `AlertaCarteraGeneradaEvent` | `cuentas_por_cobrar.alerta-cartera.generada.v1` | Notificaciones |

### 8.2 Suscritos

Tabla completa de eventos de Facturación en levantamiento §0 (7 eventos, sufijo
`IntegrationEvent`, topic `facturacion-events`, subscription
`cuentas-por-cobrar-subscription`). Futuro: `PagoClienteConfirmadoEvent` en
`tesoreria-events` (subscription `cuentas-por-cobrar-tesoreria-sub`) cuando
exista el emisor (A2).

---

## 9. Workers en-proceso (`IHostedService` en `Millet.Api`)

| Worker | Función |
|---|---|
| `OutboxPublisherWorker<CuentasPorCobrarDbContext>` | Drena outbox → `cuentas-por-cobrar-events` |
| `FacturacionEventListenerWorker` (en CxC) | `facturacion-events` → proyección `factura_cartera` + saldo 13-K; idempotente por id de origen |
| `AlertaCarteraWorker` | Evaluación programada (diaria): SOLUNION 90d, exceso de crédito, auto-bloqueo por vencimiento |
| `TesoreriaEventListenerWorker` (en CxC) | **Diferido** hasta A2 — consume `PagoClienteConfirmadoEvent` |

---

## 10. RBAC — permisos canónicos

Namespace `0000000a-*`; recurso `0001`=líneas, `0002`=liberación,
`0003`=cobranza, `0004`=cartera/reportes, `0005`=aplicación de pagos:

| GUID | Código | Descripción |
|---|---|---|
| `0000000a-0001-…-0001` | `cuentas_por_cobrar.lineas-credito.leer` | Consultar líneas de crédito |
| `0000000a-0001-…-0002` | `cuentas_por_cobrar.lineas-credito.gestionar` | Crear/editar/bloquear líneas |
| `0000000a-0002-…-0001` | `cuentas_por_cobrar.liberacion.decidir` | Decidir liberación de pedidos |
| `0000000a-0002-…-0002` | `cuentas_por_cobrar.liberacion.override` | Otorgar autorización consumible (gerente) |
| `0000000a-0003-…-0001` | `cuentas_por_cobrar.cobranza.registrar` | Registrar gestiones de cobranza |
| `0000000a-0004-…-0001` | `cuentas_por_cobrar.cartera.leer` | Cartera, antigüedad, estado de cuenta, alertas |
| `0000000a-0005-…-0001` | `cuentas_por_cobrar.aplicacion-pago.proponer` | Crear propuestas de aplicación |
| `0000000a-0005-…-0002` | `cuentas_por_cobrar.aplicacion-pago.confirmar` | Confirmar/rechazar (Ingresos, interino A2) |

---

## 11. Endpoints HTTP (resumen)

Base `/api/v1/cuentas-por-cobrar`. Mutaciones con Idempotency-Key; detalle con
ETag; errores RFC 7807.

| Recurso | Endpoints |
|---|---|
| `/lineas-credito` | GET (bandeja) · POST · GET/{id} · PUT/{id} · POST/{id}/bloquear · POST/{id}/desbloquear |
| `/liberaciones` | GET · POST (decidir) · GET/{id} |
| `/autorizaciones` | GET · POST · POST/{id}/cancelar |
| `/cobranza` | GET?clienteId= · POST |
| `/propuestas-aplicacion` | GET · POST · GET/{id} · POST/{id}/confirmar · POST/{id}/rechazar |
| `/cartera` | GET /antiguedad · GET /estado-cuenta/{clienteId} · GET /saldo-neto/{clienteId} |
| `/anticipos` | GET?clienteId= (proxy a `IFacturacionAnticiposReadPort`) |
| `/alertas` | GET · POST/{id}/atender |
| `/credito-disponible/{clienteId}` | GET (con bandera `datoIncompleto`) |

---

## 12. Frontend — patrones aplicables

Ver [`05-frontend-diseno.md`](05-frontend-diseno.md). Resumen: rutas `/cxc/*`,
patrones P1–P4 de `frontend/docs/patrones-compras.md`, reportes con
`<ReporteShell>`, nav shell de cards.

---

## 13. Dependencias de plataforma pendientes (ADR-0031)

| Pieza | Ticket | NoOp/stub en uso | Cómo se wirea |
|---|---|---|---|
| Vista A+W "liberado sin factura / en firme" (G1) | `<CreditoLiberadoSinFactura>` | Término = 0 + `dato_incompleto=true` en `CreditoDisponibleQuery` | A+W publica vista → adapter de lectura la consume y se quita la bandera |
| Emisor de `PagoClienteConfirmadoEvent` (Ingresos/Tesorería) | `<PagoClienteConfirmado>` (ya existe en `ReppEndpoints.cs`) | `ConfirmarPropuestaAplicacionPagoCommand` manual | `TesoreriaEventListenerWorker` en CxC + listener en Facturación invoca `EmitirReppCommand` |
| Contrato write-back de liberación en `MILLET_INTEGRACION` | `<CxcWriteBackLiberacion>` | Decisión persiste sin efecto en A+W | PR-9: worker consume `DecisionLiberacionEmitidaEvent` → tabla-puente |
| Feed bancario de depósitos | `<DepositoBancarioFeed>` | Captura manual del depósito en la propuesta | Integración bancaria futura (fuera de alcance v1) |
| Motor de notificaciones para `AlertaCarteraGeneradaEvent` | `<Notificaciones>` | Alerta visible solo en bandeja `/cxc/alertas` | Cuando exista el motor transversal |

---

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial derivado del levantamiento v0.2 |
