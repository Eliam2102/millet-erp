# Cuentas por Cobrar (CxC) — Ficha de administración

## Permisos canónicos (los 8 de CxC)

| Permiso | Rol sugerido |
|---|---|
| `cuentas_por_cobrar.cartera.leer` | Perfiles de consulta (cartera, antigüedad, estado de cuenta, anticipos, alertas y reportes cuelgan de este permiso) |
| `cuentas_por_cobrar.cobranza.registrar` | Analista de cobranza (gestiones + atender alertas) |
| `cuentas_por_cobrar.aplicacion-pago.proponer` | Analista de cobranza (crear propuestas, ver tolerancias y facturas abiertas) |
| `cuentas_por_cobrar.aplicacion-pago.confirmar` | **Ingresos** (confirmar/rechazar propuestas) |
| `cuentas_por_cobrar.liberacion.decidir` | Analista de crédito (bandeja/decisión de liberación) |
| `cuentas_por_cobrar.liberacion.override` | Analista de crédito senior (override consumible) |
| `cuentas_por_cobrar.lineas-credito.leer` | Consulta de líneas y crédito disponible |
| `cuentas_por_cobrar.lineas-credito.gestionar` | Analista de crédito (alta/edición/bloqueo) |

> No hay permisos dedicados de notificaciones, alertas ni reportes: las alertas
> se atienden con `cobranza.registrar` y se leen con `cartera.leer`; los
> reportes usan `cartera.leer`.

## Configuración previa obligatoria

1. **Líneas de crédito por (cliente, moneda)** (`/cxc/lineas-credito`,
   `lineas-credito.gestionar`): sin línea Activa, el pedido con serie
   `EvaluaCredito` queda `Retenido` hasta override.
2. **Comportamiento de liberación por serie** (`ReglasLiberacionSerie`):
   `SiempreLibera` (5000/7000), `NuncaLibera` (3000/4000/8000), `EvaluaCredito`
   (default). Define qué series pasan directo y cuáles evalúan crédito.
3. **Parámetros de tolerancia no fiscal** (sección
   `CuentasPorCobrar:AplicacionPagos` → `ToleranciaNoFiscal` por moneda):
   USD $50; **MXN $1,000 provisional** (gate fiscal pendiente). Moneda ausente ⇒
   tolerancia 0. El default vive dentro de la clase de opciones.
4. **Buckets de antigüedad** (sección `CuentasPorCobrar:Reportes` →
   `BucketLimites`): default `[15,30,60,90]`. ⚠️ El `ConfigurationBinder`
   **concatena** sobre el array preexistente, así que el default se deja
   **vacío** en el POCO y el real vive en `BucketsAntiguedadCxc.LimitesDefault`;
   configurar límites no ascendentes rompe el reporte con HTTP 500 (incidente
   2026-07-14).
5. **Seed de los 8 permisos** en `IdentidadDbContext` (namespace GUID
   `0000000a-*`).

## Eventos que publica / consume

| Dirección | Evento | Con quién | Efecto operativo |
|---|---|---|---|
| Publica | `cuentas_por_cobrar.propuesta-aplicacion.creada.v1` | Tesorería | Depósito propuesto a confirmar (extensión aditiva con `Facturas[]`, TES-PR7) |
| Publica | `cuentas_por_cobrar.alerta-cartera.generada.v1` | (interno / notificaciones) | Alerta de cartera generada por el worker diario |
| Publica | `cuentas_por_cobrar.decision-liberacion.emitida.v1` | A+W (write-back) | ⚠️ **BLOQUEADO** (CXC-PR9, write-back A+W pendiente) |
| Consume | `facturacion.factura-venta.timbrada.v1` | `FacturacionEventListenerWorker` | Proyecta `factura_cartera` (Abierta) |
| Consume | `facturacion.recibo-pago.timbrado.v1` | idem | Aplica pago (REPP) a cartera |
| Consume | `facturacion.cobro-mostrador.registrado.v1` / `.cancelado.v1` | idem | Aplica/reversa cobro de caja |
| Consume | `facturacion.nota-credito.timbrada.v1` | idem | NC ranura (01) / amortización (07) |
| Consume | `facturacion.factura-anticipo.timbrada.v1` | idem | Anticipo disponible |
| Consume | `facturacion.comprobante.cancelado.v1` | idem | Reversa de cartera |

## Monitoreo y troubleshooting

- **Workers en Application Insights**: `FacturacionEventListenerWorker` (dedupe +
  dead-letter, switch por `EventType`) y `AlertaCarteraWorker` (evaluación
  periódica, `IntervalHours` configurable). Si una factura timbrada no aparece
  en cartera, revisar el listener y los dead-letters de la subscription de
  Facturación.
- **Idempotencia**: los consumidores correlacionan por la clave de negocio del
  comprobante; reprocesar un evento es seguro.
- **Antigüedad en 500**: casi siempre buckets mal configurados (ver
  Configuración previa §4).

## Límites conocidos vigentes

- **`TesoreriaEventListenerWorker` en CxC NO existe todavía** (diferido a A2): el
  consumo automático de `tesoreria.pago-cliente.confirmado.v1` no está; la
  confirmación de la propuesta se hace **manual** desde la bandeja.
  PLATFORM-TODO `<PagoClienteConfirmado>`.
- **T-G7 / `PropuestaRechazadaConsumerCxC`**: cuando Tesorería rechaza un
  depósito (`tesoreria.propuesta-aplicacion.rechazada.v1`), CxC **aún no lo
  consume** para re-proponer; hoy se resuelve con endpoint interino.
  PLATFORM-TODO `<PropuestaRechazadaConsumerCxC>`.
- **Write-back de liberación a A+W** (CXC-PR9) **bloqueado** por el gap de A+W.
- **Tolerancia MXN provisional** ($1,000) sujeta a gate fiscal.
