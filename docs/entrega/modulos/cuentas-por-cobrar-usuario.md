# Cuentas por Cobrar (CxC) — Ficha de usuario

## Qué hace el módulo

Lleva la cartera de clientes: recibe cada factura timbrada de Facturación y la
proyecta a `factura_cartera`, la reduce con los cobros (mostrador, REPP, notas
de crédito) y la reporta por antigüedad de saldos. Gestiona las **líneas de
crédito** por cliente y modera la **liberación de pedidos** con una cascada
serie → crédito → override. Propone la **aplicación de los depósitos** de
clientes que luego confirma Tesorería. **No** emite CFDI ni REPP (eso es de
Facturación), no registra los movimientos bancarios ni confirma depósitos
contra el banco (Tesorería) y no hace asientos contables.

## Quién lo usa

| Rol | Qué hace aquí |
|---|---|
| Analista de cobranza | Consulta cartera/antigüedad, registra gestiones de cobranza, atiende alertas, propone aplicaciones de pago |
| Analista de crédito | Alta y mantenimiento de líneas de crédito; decide/retiene liberación de pedidos; emite overrides |
| Ingresos (Tesorería) | Confirma o rechaza las propuestas de aplicación (permiso `aplicacion-pago.confirmar`) |

## Operaciones principales

| Operación | Pantalla | Pasos resumidos |
|---|---|---|
| Ver cartera y antigüedad | `/cxc/cartera` | Filtros por cliente/moneda; buckets de días vencidos + `por_vencer` |
| Estado de cuenta por cliente | `/cxc/estado-cuenta` | Detalle de facturas, cobros, NC y anticipos del cliente |
| Proponer aplicación de un depósito | `/cxc/aplicaciones` → **"Nueva propuesta"** | Cliente, referencia del depósito, monto, facturas con parcialidad; opcional **ajuste no fiscal** (comisión bancaria) |
| Decidir liberación de un pedido | `/cxc/liberaciones` | Por fila: la cascada resuelve Liberado/Retenido; retenido se libera con **override** |
| Emitir override de crédito | `/cxc/liberaciones` | Autorización consumible (permiso `liberacion.override`) |
| Líneas de crédito | `/cxc/lineas-credito` | Alta/edición, bloquear/desbloquear por (cliente, moneda) |
| Registrar gestión de cobranza | `/cxc/cobranza` | Bitácora de contacto/compromiso de pago |
| Atender alertas de cartera | `/cxc/alertas` | Marcar **"Atender"** las alertas generadas por el worker diario |

## Flujos internos de control

- **Proyección de cartera**: al timbrarse una factura llega a `factura_cartera`
  en estado `Abierta`; con cobros/NC pasa a `Parcial`, `Pagada` o `Cancelada`.
- **Cascada de liberación** (`DecidirLiberacionCommand`):
  1. Series **SiempreLibera** (5000/7000) → `Liberado` sin evaluar crédito.
  2. Series **NuncaLibera** (3000/4000/8000) → `Retenido` salvo override →
     `LiberadoConOverride`.
  3. **EvaluaCredito** (default): línea Activa del (cliente, moneda) con
     disponible ≥ monto → `Liberado`; si no, `Retenido` salvo override.
- **Tolerancia no fiscal en la aplicación de pagos**: la suma de facturas +
  ajuste no fiscal debe igualar el depósito; el ajuste solo puede ser **negativo**
  (comisión bancaria). El tope por moneda hoy es USD $50 y MXN $1,000
  (**provisional**, pendiente de gate fiscal).
- **Confirmación de propuesta**: hoy es **manual** (Ingresos confirma o rechaza).
  El consumo automático de `tesoreria.pago-cliente.confirmado.v1` está
  **diferido** (llega en una fase posterior).

## Ejemplos con folios reales

- Ruta bancaria completa (factura PPD → propuesta CxC → depósito Tesorería →
  REPP automático → cartera saldada): [P9](../pipelines/p9-cajero-facturacion-tesoreria.md),
  Fase 6 — factura `VEN-000016` ($1,392), propuesta `SPEI-P9-VERIF-001`.
- Multimoneda (cartera MXN mostrador/obra + cartera USD exportación):
  [P10](../pipelines/p10-facturacion-multimoneda-cce-obra.md).

## Errores comunes (en lenguaje de negocio)

- **"El pedido está retenido por crédito insuficiente."** — la línea del
  (cliente, moneda) no alcanza; se libera con override o ampliando la línea.
- **"La suma de facturas y el ajuste no fiscal no cuadra con el depósito."** —
  la propuesta debe cumplir `Σ facturas + ajuste = depósito`; el ajuste solo
  resta (comisión bancaria dentro de tolerancia).
- **"La antigüedad de saldos devuelve error 500."** — si los cortes de buckets
  quedan mal configurados (no ascendentes) el reporte falla; revisar la sección
  `CuentasPorCobrar:Reportes` (incidente 2026-07-14 por concatenación del array).
- **"El depósito propuesto fue rechazado en Tesorería."** — hoy la re-propuesta
  se hace a mano; el consumidor automático del rechazo está pendiente (T-G7).
