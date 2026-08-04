# Tesorería (Bancos) — Ficha de usuario

## Qué hace el módulo

Registra todo peso que entra o sale por los bancos de la empresa: paga los
pasivos que CxP autorizó (individual o por pago a cuenta), confirma los
depósitos de clientes que propone CxC (lo que dispara la emisión del REPP en
Facturación), vigila que los proveedores entreguen su complemento de pago (REPP,
SLA de 5 días) y lleva el libro de movimientos por cuenta bancaria. **No**
decide qué facturas pagar (eso es de CxP), no emite CFDI ni REPP propios (eso es
de Facturación), no hace asientos contables y no tiene todavía conector de banca
en línea (los pagos se ejecutan en el portal del banco y aquí se registran).

## Quién lo usa

| Rol | Qué hace aquí |
|---|---|
| Auxiliar de Tesorería | Registra movimientos y pagos, captura referencias, registra REPP recibidos, propone ligas de pagos a cuenta |
| Jefe de Tesorería | Autoriza pagos a cuenta y ligas con diferencia; (corridas y conciliación cuando existan en pantalla) |
| Tesorería-Ingresos | Confirma o rechaza depósitos de clientes |

## Operaciones principales

| Operación | Pantalla | Pasos resumidos |
|---|---|---|
| Pagar pasivos autorizados | `/tesoreria/pagos` ("Pagos a proveedor") | Seleccionar pasivos **del mismo proveedor** → **"Registrar pago (N)"** → cuenta, fecha, referencia → **"Registrar pago"** |
| Pago a cuenta (sin documento) | `/tesoreria/pagos-cuenta` → **"Nuevo pago a cuenta"** | Egreso sin pasivo; máximo **uno abierto por proveedor**; se liga después con "Ligar pago a cuenta al pasivo" |
| Confirmar depósitos | `/tesoreria/depositos` ("Depósitos por confirmar") | Por fila: **"Confirmar"** contra el movimiento de ingreso del banco, o **"Rechazar"** con motivo para que CxC re-proponga |
| Registrar REPP recibido | `/tesoreria/repp` ("REPP de proveedor") | Filtro "Solo vencidos (>5 días)"; **"Registrar REPP"** → UUID del complemento + XML → libera el motivo FALTA_REPP en CxP |
| Registrar ingreso manual | `/tesoreria/movimientos` | Alta de movimiento de ingreso identificado |
| Consultar el libro | `/tesoreria/movimientos` y detalle | Filtros por cuenta y sentido; el detalle muestra aplicaciones a pasivos y contramovimientos |
| Revertir un pago | Detalle del movimiento | Genera contramovimiento con motivo; nunca borra |
| Reportes | `/tesoreria/reportes/auxiliar-bancos`, `/flujo-efectivo` | Consulta y exportación |

> **Corridas de pago** (`/tesoreria/corridas`) y **conciliación bancaria**
> (`/tesoreria/conciliacion`) aparecen en el menú pero **aún no tienen pantalla
> operable** (placeholder). Sus flujos están diseñados y llegarán en PRs
> posteriores; mientras tanto los pagos se registran individualmente.

## Flujos internos de control

- **Pago a cuenta con liga tardía** (`/tesoreria/pagos-cuenta`): egreso sin
  documento con dos candados — máximo **uno abierto por proveedor** (RN-2) y
  liga posterior al pasivo cuando CxP lo provisiona; las ligas con diferencia
  de monto las confirma el Jefe de Tesorería.
- **Reversa de pago** (RN-10): siempre con motivo y por **contramovimiento** —
  el movimiento original nunca se borra; el detalle muestra la cadena
  "Contramovimiento de".
- **Enmascaramiento PII**: CLABE y número de cuenta viajan enmascarados; verlos
  completos exige el permiso específico `tesoreria.movimientos.ver-cuenta-completa`.
- **Aplicación por factura** (RN-4): un movimiento puede cubrir N pasivos del
  mismo proveedor, pero el evento hacia CxP se emite **por factura** — la
  trazabilidad en el detalle del movimiento es 1 movimiento : N aplicaciones.

## Ejemplos con folios reales

- **Pago `SPEI-P1-VERIF`** — pago del pasivo de la factura `P1-001`
  ($1,350.00) desde la bandeja, y el registro posterior del **REPP** del
  proveedor: [P1](../pipelines/p1-flujo-feliz-insumos.md), pasos 11–12.
- El pasivo de la factura `P2-001` llegó a la bandeja por su **saldo neto**
  ($500.50) tras amortizar el anticipo `ANT-P5`:
  [P5](../pipelines/p5-anticipos.md).

## Errores comunes (en lenguaje de negocio)

- **"Pasivo(s) no presentes en la bandeja de autorizados..."** — solo se paga lo
  que CxP autorizó; si falta, el pendiente está en CxP, no aquí.
- **"Un pago cubre pasivos de un solo proveedor; registra un pago por
  proveedor."** — la selección multi-proveedor no es válida.
- **"La cuenta es MXN y hay pasivos en USD..."** — pagos cross-moneda fuera del
  MVP; usar una cuenta de la moneda del pasivo.
- **"El proveedor ya tiene un pago a cuenta abierto; liga el anterior antes de
  registrar otro."** — regla RN-2.
- **"El movimiento (X) no coincide con el depósito propuesto (Y); si el banco
  recibió otro importe, rechaza la propuesta para que CxC re-proponga."** — la
  confirmación de depósitos exige coincidencia exacta.
- **"Solo un movimiento bancario de ingreso puede confirmar un depósito."** — no
  se confirma contra egresos ni contramovimientos.
- **"El complemento con UUID '...' ya está registrado."** — REPP duplicado.
- **"El período AAAA/MM está cerrado."** — no se registran movimientos en
  periodo contable cerrado.
