# P5 — Anticipos a proveedor (CFDI serie FANT) y aplicación a factura

**Cadena:** Captura del CFDI de anticipo → amortización contra una factura →
saldo neto de la factura.

**Evidencia en dev (2026-07-15):** anticipo `ANT-P5` ($500.00) amortizado contra
la factura `P2-001` ($1,000.50) → saldo pendiente **$500.50**.

## Objetivo de negocio

Cuando Millet entrega dinero por adelantado a un proveedor (autorizado por
Dirección), el proveedor emite un **CFDI de anticipo (serie FANT)**. Ese
anticipo queda como saldo a favor y se **amortiza** contra facturas
posteriores del mismo proveedor: la factura se paga solo por el neto.

## Actores y permisos

| Paso | Actor | Permisos requeridos |
|---|---|---|
| Autorizar la entrega del anticipo | Dirección General / Dirección de Finanzas | (fuera del sistema en MVP: siempre autoriza Dirección, sin importe mínimo) |
| Capturar el CFDI de anticipo | Auxiliar de CxP | `cuentas_por_pagar.anticipos.capturar`, `.leer` |
| Amortizar contra factura | Auxiliar de CxP | `cuentas_por_pagar.facturas.capturar` / edición del pasivo |

## Precondiciones

1. CFDI del proveedor **con serie FANT** — la serie se valida: otra serie
   rechaza con `ANTICIPO_SERIE_INVALIDA`.
2. Factura destino del mismo proveedor en estado que acepte aplicaciones
   (capturada/autorizada, no cancelada ni pagada).

## Pasos

1. **Capturar el anticipo.** En `/cxp/anticipos` ("Anticipos"), botón
   **"Capturar anticipo"** → Sheet "Capturar anticipo a proveedor": proveedor,
   CFDI FANT (UUID), monto ($500.00). Queda `Abierto` con saldo amortizable
   $500.00 (evidencia: `ANT-P5`).
   📸 Captura pendiente: Sheet "Capturar anticipo a proveedor".
2. **Amortizar contra la factura.** Desde el detalle de la factura destino
   (`/cxp/facturas/$id`, aquí `P2-001`), aplicar el anticipo: el sistema
   descuenta $500.00 del saldo de la factura y del saldo amortizable del
   anticipo.
   📸 Captura pendiente: detalle de factura con aplicación de anticipo.
3. **Resultado.** Factura `P2-001`: total $1,000.50 − anticipo $500.00 =
   **saldo pendiente $500.50**. El anticipo `ANT-P5` queda `Amortizado`
   (saldo amortizable $0). Tesorería verá el pasivo por el **neto**.

> **Eventos:** la amortización es interna a CxP (aplicación sobre el pasivo).
> Hacia afuera, el pasivo que llega a Tesorería vía `PasivoAutorizadoParaPagoEvent`
> ya refleja el saldo neto.

## Cómo verificar el resultado en cada módulo

| Módulo | Dónde | Qué esperar |
|---|---|---|
| CxP | `/cxp/anticipos` | `ANT-P5` en `Amortizado`, saldo $0; trazabilidad hacia `P2-001` |
| CxP | `/cxp/facturas/$id` | `P2-001` con aplicación de anticipo por $500.00 y saldo pendiente $500.50 |
| CxP | `/cxp/reportes/antiguedad-anticipos` | El anticipo ya no aparece como saldo abierto |
| Tesorería | `/tesoreria/pagos` | El pasivo de `P2-001` aparece por $500.50 (neto) |

## Variantes y errores esperados

| Situación | Error real (422) | Mensaje |
|---|---|---|
| CFDI con serie distinta a FANT | `ANTICIPO_SERIE_INVALIDA` | "La serie del CFDI de anticipo debe ser '{SerieEstandar}' (recibida: '{serie}')." |
| Mismo UUID capturado dos veces | `ANTICIPO_DUPLICADO` | "Ya existe un anticipo con UUID '{uuid}'." |
| Amortizar más que el saldo | `ANTICIPO_SALDO_INSUFICIENTE` | "El monto a amortizar ({monto}) excede el saldo amortizable ({SaldoAmortizable})." |
| Amortizar un anticipo cancelado | `ANTICIPO_CANCELADO` | "No se puede amortizar un anticipo cancelado." |
| Aplicar más que el saldo de la factura | `APLICACION_EXCEDE_SALDO` | "Aplicar {monto} dejaría el saldo negativo (saldo actual: {SaldoPendiente})." |
| Factura en estado que no acepta anticipos | `FACTURA_NO_ACEPTA_ANTICIPO` | La factura debe estar en un estado aplicable (no cancelada/pagada) |
| Cancelar un anticipo ya amortizado | `ANTICIPO_AMORTIZADO_NO_CANCELABLE` | Un anticipo consumido no se cancela; el reverso va por otro flujo |
