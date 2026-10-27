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
| `IFacturacionAplicacionesAnticipoReadPort` | Facturación (`FacturacionAplicacionesAnticipoReadAdapter`, F1-CXC-05) | Anticipos con sus aplicaciones a factura final (§11.2) |
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
| `/cartera` | GET /antiguedad · GET /estado-cuenta/{clienteId} · GET /estado-cuenta/{clienteId}/pendientes · GET /estado-cuenta/{clienteId}/envios · POST /estado-cuenta/{clienteId}/envios (multipart, C7.8) · GET /saldo-neto/{clienteId} |
| `/anticipos` | GET?clienteId= (proxy a `IFacturacionAnticiposReadPort`) · GET /aplicaciones?clienteId=&estado= (F1-CXC-05, §11.2) |
| `/alertas` | GET · POST/{id}/atender |
| `/credito-disponible/{clienteId}` | GET (con bandera `datoIncompleto`) |

### 11.1 Estado de cuenta al cliente (F1-CXC-09 · C7.8)

- **Bloqueo (CA7.14).** Una sola regla (`EstadoCuentaBloqueo`) para el reporte,
  los pendientes y el envío: no se emite ni se envía mientras el cliente tenga
  un pago confirmado en `ReppPendiente` (Facturación) en estado *Pendiente* o un
  REP emitido cuyo `ReciboPagoId` aún no aparece como `OrigenComprobanteId` en
  `movimiento_cartera`. Responde 422 `ESTADO_CUENTA_REPP_PENDIENTE`; el POST lo
  recalcula en servidor y no registra nada.
- **Pendientes.** `GET .../estado-cuenta/{clienteId}/pendientes` →
  `{ emitible, pendientes[], recibosSinReflejar[], emailCliente }` (detalle vía
  `IReppPendientesReadPort`, sin tocar tablas de Facturación). La pantalla lo
  muestra en lugar del reporte con enlace a `/facturacion/repp/pendientes`.
- **Envío.** `POST .../estado-cuenta/{clienteId}/envios` (multipart `pdf` +
  `destinatario` + `mensaje`; Idempotency-Key; permiso
  `cuentas_por_cobrar.cobranza.registrar`; alcance `cliente_cartera`). El PDF
  lo genera el navegador con el exportador del reporte (ADR-0036); el backend
  guarda en `estado_cuenta_envio` destinatario, usuario, fecha, totales por
  moneda (jsonb), número de filas, SHA-256 y nombre del PDF, y el estado
  *Registrado* / *Enviado* / *Fallido*. Un fallo del correo no revierte el
  registro. `GET .../envios` devuelve el historial (permiso `cartera.leer`).
- **Correo.** Puerto `IEstadoCuentaCorreoPort`: SMTP de captura en loopback
  (Mailpit) solo en Development (`CuentasPorCobrar:EstadoCuentaCorreo:Proveedor=SandboxLocal`);
  en los demás ambientes NoOp → queda *Registrado* (ver §13).
- **Fuera de alcance:** NC sin proyectar en cartera (no bloquean), cobros de
  mostrador que no pasan por `ReppPendiente`, y los totales del encabezado del
  reporte que suman MXN y USD (el registro del envío sí los guarda por moneda).

### 11.2 Anticipos con su aplicación a la factura final (F1-CXC-05)

- **Solo consulta (R11).** CxC muestra el saldo de cada anticipo y en qué
  facturas finales se aplicó; la emisión, la vinculación y la amortización
  (CFDI de egreso tipo 07) siguen en Facturación.
- **Endpoint.** `GET /anticipos/aplicaciones?clienteId=&estado=` (permiso
  `cuentas_por_cobrar.cartera.leer`). `clienteId` es opcional: sin él lista
  todos los clientes con anticipos; si viene no puede ser `Guid.Empty` (400).
  `estado` ∈ Pendiente/Parcial/Aplicado/Cancelado. Respuesta
  `{ anticipos[], totalesPorMoneda[] }`; cada anticipo trae folio/UUID/fecha
  del CFDI de anticipo, sucursal, moneda, `cobrado`, `aplicado`,
  `comprometido`, `saldo`, `saldoSistema`, `estado`, `observacion` y
  `aplicaciones[]` (factura final folio/UUID, egreso 07 folio/UUID, fecha,
  estado del egreso, importe y tipo).
- **Alcance por sucursal (ADR-0051).** La query es `ISucursalScopedQuery` con
  el bypass `cuentas_por_cobrar.cartera.leer-todas-sucursales`; filtra por la
  sucursal del CFDI de anticipo. No aplica el alcance de Cajas de Facturación.
- **Cálculo.** El adapter de Facturación copia las reglas de la comparativa
  F6.2 (`ComparativaAnticiposHandler`) a la fecha de hoy: solo un egreso
  Timbrado o en cancelación pendiente resta saldo; un egreso cancelado se
  muestra y no resta; una vinculación sin egreso timbrado va a
  «Comprometido». `saldo = cobrado − aplicado`. Una prueba de paridad
  (`AplicacionesAnticipoReadAdapterTests`) amarra ambos cálculos: si cambia una
  regla en F6.2, cambia aquí. Los totales son por moneda y excluyen anticipos
  cancelados.
- **Pantalla.** `/cxc/anticipos` (misma ruta): cliente opcional, pestañas por
  estado, fila expandible «Aplicaciones a factura final», KPIs por moneda y
  enlace a `/facturacion/reportes/comparativa-anticipos` solo con
  `facturacion.reportes.leer`.
- **Fuera de alcance:** la restitución del saldo del anticipo al cancelar el
  egreso 07 (reglas 86e3tfcm4 / 86e3tfd6n). Mientras no exista, la diferencia
  entre el saldo calculado y el persistido se muestra como observación
  «Saldo en sistema …», sin corregirla.

### 11.3 Depósitos sin identificar y parcialidades (F1-CXC-03 · C7.4)

- **Criterios.** CA7.9: un depósito que llega sin detalle queda «sin
  identificar» y no se aplica hasta tener el detalle. CA7.8: un depósito de
  $50,000 que el cliente detalla contra 3 facturas se propone en CxC, lo
  confirma Tesorería y las facturas se saldan al timbrarse el REP; quien
  propone no confirma. C7.4-a: un pago parcial de una PPD registra la
  parcialidad y el saldo insoluto para el REP. Reglas R13 (sin remittance no se
  propone; pagos parciales como parcialidad) y R12 (CxC propone, Tesorería
  confirma).
- **Flujo.** Tesorería registra el ingreso como «Cobro de cliente» (cliente
  opcional; `BeneficiarioTipo=Cliente`). CxC lo ve en la pestaña «Sin
  identificar» de `/cxc/aplicaciones` (`?vista=sin-identificar`, sin ruta
  nueva) vía `GET /propuestas-aplicacion/depositos-por-identificar` (permiso
  `aplicacion-pago.proponer`), y Tesorería en la pestaña «Sin identificar» de
  su bandeja de depósitos vía `GET /api/v1/tesoreria/depositos/sin-identificar`
  (permiso existente `tesoreria.movimientos.ver`; alcance por sucursal con
  `tesoreria.documentos.leer-todas-sucursales`). Ambas bandejas muestran «Días
  en espera» (fecha valor → hoy) y resaltan los de más de 5 días
  (`DIAS_ESPERA_ALERTA`, supuesto de la ficha §7.4; no bloquea). «Identificar»
  aparece solo a quien tiene `aplicacion-pago.proponer`. «Identificar» abre la nueva propuesta
  prellenada; identificar = proponer con remittance (`POST
  /propuestas-aplicacion` con `movimientoBancarioId`). Tesorería confirma con
  otro usuario (`DEP_MISMO_USUARIO`), Facturación emite el REP y CxC salda la
  cartera al consumir `recibo-pago.timbrado.v1`.
- **Puerto.** `IDepositosPorIdentificarReadPort` (CxC, `Domain/Ports/Tesoreria`)
  implementado por `DepositosPorIdentificarReadAdapter` en Tesorería (mismo
  patrón que `IReppPendientesReadPort` en Facturación; Tesorería referencia a
  CxC, nunca al revés). «Sin identificar» = ingreso, no contramovimiento ni
  revertido, beneficiario Cliente, con disponible > 0, sin devolución
  solicitada/autorizada/pagada y no ligado a caja o viáticos. La bandeja de CxC
  descuenta las propuestas en estado Propuesta contra el mismo depósito; las
  que Tesorería ya confirmó (el puerto las informa en `PropuestasConfirmadas`)
  no se descuentan dos veces mientras llega el evento de confirmación.
- **Validaciones al identificar.** `PAP_DEPOSITO_NO_DISPONIBLE` (ya aplicado,
  devuelto o revertido), `PAP_DEPOSITO_MONEDA_DISTINTA`,
  `PAP_DEPOSITO_EXCEDE_DISPONIBLE` (monto > disponible − propuestas
  pendientes), `PAP_DEPOSITO_DE_OTRO_CLIENTE` (Tesorería ya lo registró a otro
  cliente) y `PAP_SIN_REMITTANCE` (sigue obligatorio). Alcance por sucursal
  del depósito (ADR-0051): el operativo solo ve/identifica depósitos de sus
  sucursales; un depósito sin sucursal solo lo ve el corporativo.
- **Sobrante.** Si la propuesta deja saldo a favor por identificar, Tesorería
  aplica solo lo propuesto: el movimiento queda `AplicadoParcial` y el sobrante
  vuelve a la bandeja «Sin identificar» para otra propuesta. Sin migración: lo
  aplicado se deriva de las confirmaciones del movimiento. **E9 (C7.4-b):** si
  el ingreso llegó sin cliente, el sobrante reaparece como «sin identificar»
  del cliente de la última propuesta confirmada contra él; sin CFDI ni anticipo
  (la conversión a anticipo queda fuera, pregunta a Millet).
- **E8 · liga con el ingreso.** Si la propuesta trae `MovimientoBancarioId`,
  Tesorería solo la confirma contra ese movimiento
  (`DEP_MOVIMIENTO_DISTINTO_AL_LIGADO`); la pantalla fija el movimiento y no
  ofrece alta rápida. Sin liga sigue la regla anterior.
- **Parcialidades.** La «parcialidad siguiente» y el saldo de una factura PPD
  salen de Facturación con el mismo cálculo que
  `FacturasCobrablesPpdQuery.NumParcialidadSiguiente` (Total − NC − REP
  vigentes; REP vigentes + 1), leído por `IReppPendientesReadPort.ParcialidadesAsync`
  (`SaldoPorCobrar.ParcialidadesAsync` compartido). **Decisión:** las propuestas
  abiertas de CxC (Propuesta, o Confirmada sin REP) suman a la parcialidad y
  restan del disponible, porque cada una será un REP antes que la nueva. Si
  Facturación no conoce la factura, CxC usa su cartera (pagos vigentes + 1). Se
  muestra en las facturas abiertas (`numParcialidadSiguiente`, `disponible`),
  en la respuesta y en el detalle («Parc. n · saldo insoluto»), y se guarda en
  la columna existente `num_parcialidad`; el número que mande el cliente se
  ignora. Facturación sigue asignando el número del REP al timbrar. Un pago
  parcial a una factura que no es PPD se rechaza (`PAP_PARCIAL_NO_PPD`).
- **REP ↔ propuesta.** Facturación guarda un `repp_pendiente` por propuesta
  confirmada (migración `ReppPendientePorPropuesta`: índice único
  `(movimiento_bancario_id, propuesta_id)`; sin propuesta sigue uno por
  movimiento), así que un depósito identificado en partes genera un REP por
  cada propuesta. `recibo-pago.timbrado.v1` agrega `PropuestaId` (aditivo) y
  `ReciboPagoTimbradoHandler` marca exactamente esa propuesta; sin
  `PropuestaId` (eventos anteriores) toma la **Confirmada** del movimiento que
  aún no tiene REP (antes tomaba la primera, aunque estuviera rechazada).
- **«Sugerir» (V61).** Las facturas abiertas se listan de la más antigua a la
  más reciente (vencimiento, luego timbrado). «Sugerir» llena importes por
  antigüedad sin exceder el disponible ni el monto, **sin enviar**; los importes
  quedan editables y una persona propone. Ningún cobro se aplica sin selección
  humana (V61, Millet, junta del 8-oct; sustituye a P7.4).
- **Evento.** `propuesta-aplicacion.creada.v1` agrega `MovimientoBancarioId`
  (opcional, aditivo); Tesorería lo usa para preseleccionar el movimiento.
- **Fuera de alcance:** la importación automática de depósitos desde el banco
  (`PLATFORM-TODO(<DepositoBancarioFeed>)`, T9.2/T9.4).

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
| Feed bancario de depósitos | `<DepositoBancarioFeed>` | Tesorería registra el ingreso a mano («Cobro de cliente», cliente opcional); CxC lo identifica desde «Sin identificar» (C7.4) | Importación de estados de cuenta / conciliación bancaria (T9.2/T9.4) alimenta el mismo `MovimientoBancario`; el puerto `IDepositosPorIdentificarReadPort` no cambia |
| Motor de notificaciones para `AlertaCarteraGeneradaEvent` | `<Notificaciones>` | Alerta visible solo en bandeja `/cxc/alertas` | Cuando exista el motor transversal |
| Correo saliente del estado de cuenta al cliente (C7.8) | `<EnvioEstadoCuentaCliente>` | `NoOpEstadoCuentaCorreoAdapter` (el envío queda *Registrado*; el PDF se descarga). En Development, `SandboxLocalEstadoCuentaCorreoAdapter` → Mailpit | Adaptador de `IEstadoCuentaCorreoPort` sobre el motor de correo transversal (Graph/SMTP corporativo, ADR-0026) |

---

## Rev.

| Versión | Fecha | Cambio |
|---|---|---|
| v0.1 | 2026-07-13 | Borrador inicial derivado del levantamiento v0.2 |
| v0.2 | 2026-10-20 | F1-CXC-09 · C7.8: envío del estado de cuenta, bloqueo por REP pendientes (§11.1) y PLATFORM-TODO `<EnvioEstadoCuentaCliente>` (§13) |
| v0.3 | 2026-10-21 | F1-CXC-05: anticipos con su aplicación a la factura final (§6.1, §11, §11.2) |
