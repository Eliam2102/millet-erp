# IVA por flujo de efectivo y liquidación mensual (C1.6 y K10.7)

> Tarea F1-CON-10 (86e3jvvm9). Rev 1.0, 4-nov-2026.

## 1. Qué resuelve

La LIVA causa el IVA cuando se paga o se cobra. El motor ya dejaba el IVA en cuentas de **pendiente**:

| Documento | Póliza | Cuenta de IVA |
|---|---|---|
| Factura de proveedor | #2 | `IVA_ACREDITABLE_PENDIENTE` |
| Factura de venta | #7 | `IVA_TRASLADADO_NO_COBRADO` |
| Factura de anticipo | #8 | `IVA_TRASLADADO_NO_COBRADO` |

Con C1.6, cada pago o cobro pasa a **pagado** o **cobrado** el IVA que le corresponde. K10.7 suma esos movimientos por mes, los cuadra contra las cuentas y arma el borrador de la póliza de liquidación.

## 2. C1.6 · Reclasificación al pagar o cobrar

### 2.1 Saldo de IVA por documento

Las tablas son `contabilidad.saldos_iva_documento` y `contabilidad.reclasificaciones_iva`.

- La póliza de cada factura (#2, #7 y #8) deja su `SaldoIvaDocumento` con esta información:
  - la **base**: total menos retenciones, que es lo que se paga;
  - el IVA y el IVA retenido (impuesto `002`);
  - lo aplicado y el IVA ya reclasificado;
  - el UUID y el método de pago.
- **Cálculo:** IVA reclasificado = IVA × aplicado ÷ base, redondeado a centavos. El pago que salda el documento toma el **remanente**, así que no quedan centavos en pendiente. Por ejemplo, tres pagos iguales de 1,160 dan 53.33, 53.33 y 53.34. El IVA retenido se reclasifica en la misma proporción.
- Cada pago o cobro deja una `ReclasificacionIva`. Guarda el IVA en la moneda del documento y en MXN, con el tipo de cambio de la póliza del pago.
- **Idempotencia:** hay una fila por pago y documento, con índice único. Un reproceso usa lo que ya se guardó.

### 2.2 Plantillas v2

Se siembran por migración y no cambian las pólizas pasadas. Cada plantilla v2 es la v1 más dos líneas sobre el campo `IvaReclasificado`:

| Evento | Cargo | Abono |
|---|---|---|
| #5 Pago a proveedor | `IVA_ACREDITABLE_PAGADO` | `IVA_ACREDITABLE_PENDIENTE` |
| #12 Pago de cliente | `IVA_TRASLADADO_NO_COBRADO` | `IVA_TRASLADADO_COBRADO` (rol nuevo) |
| #11a Cobro de mostrador, parte no efectivo | igual que #12 | igual que #12 |
| #11b Liquidación de efectivo de la sesión | igual que #12 | igual que #12 |

- En caja, el IVA del **efectivo** se reclasifica en la póliza de liquidación de la sesión (#11b). Así se conserva el criterio de C1.5: el efectivo no genera pólizas sueltas.
- Un cobro mixto deja dos movimientos: el de la parte no efectivo y el del efectivo, este con el tipo `cobro_mostrador_efectivo`.
- Si el documento no tiene IVA, o se contabilizó antes de C1.6, el campo va en cero y la plantilla no genera esas líneas.

### 2.3 Orden de llegada y reversas

- **Pago antes que su documento:** un pago, o un cobro no efectivo, que llega antes que la póliza de su documento espera en la bandeja como dato faltante. Se reprocesa solo cuando nace esa póliza; es el mismo mecanismo de C1.5. La liquidación de efectivo nunca espera: si un comprobante no tiene saldo, no reclasifica.
- **C1.6-b, reversas:**
  - **Pago a proveedor revertido:** la anulación de su póliza #5 ya revierte las partidas. Además, el movimiento queda anulado y el saldo vuelve a pendiente.
  - **Cobro cancelado:**
    - La parte no efectivo se anula junto con su póliza.
    - Si el efectivo ya estaba liquidado en la sesión, se reversa con el IVA en negativo.
- **Notas de crédito y de cargo (#3b, #3c y #9 de descuento):** reducen la base y el IVA del documento relacionado. Su póliza ya revierte ese IVA pendiente.
- **Documento cancelado:** su saldo queda cancelado y no reclasifica.

### 2.4 Retiro del stub de Facturación

Se retiraron `IContabilidadAsientoPort` y su NoOp. Contabilidad arma las pólizas de Facturación desde sus eventos (C1.5).

## 3. K10.7 · Liquidación mensual de IVA

Página: Contabilidad › Reportes › Liquidación de IVA (`/contabilidad/reportes/liquidacion-iva`). Usa los permisos de pólizas y no crea permisos nuevos.

### 3.1 Resumen del mes (MXN)

| Concepto | Signo |
|---|---|
| IVA trasladado cobrado | + |
| IVA acreditable pagado | − |
| IVA retenido por clientes | − |
| **IVA a cargo / a favor** | = |
| IVA retenido a proveedores (fletes) | No resta. Se muestra aparte como «por enterar». |

### 3.2 Cuadre (K10.7-a)

Por cada rol (`IVA_TRASLADADO_COBRADO` e `IVA_ACREDITABLE_PAGADO`) se compara:

- la suma por documentos;
- el movimiento del mes en las cuentas del rol, sin borradores.

Una diferencia indica pólizas manuales o ajustes en esas cuentas, o un pago revertido en otro mes.

### 3.3 Detalle por documento (K10.7-b)

Cada documento con IVA pagado o cobrado en el mes queda «Completo» o «Pendiente», con su motivo:

- Sin CFDI.
- Con saldo por pagar o por cobrar.
- PPD pagado sin REP.
- Documento cancelado.

De dónde sale cada dato:

- **Método de pago y REP del proveedor:** del puerto público de CxP (`IFacturaProveedorFiscalReadPort`).
- **REP timbrado de la factura de venta:** de Facturación (`IRepEmitidoReadPort`).
- Los compone `CumplimientoIvaReadAdapter` en Api.

### 3.4 Póliza de liquidación

Se crea como **borrador**, tipo AS, con fecha del último día del mes y la sucursal que se elija. El contador la revisa y la autoriza.

| Partida | Rol |
|---|---|
| Cargo | `IVA_TRASLADADO_COBRADO` |
| Abono | `IVA_ACREDITABLE_PAGADO` |
| Abono | `RETENCIONES_CLIENTES`, por el IVA retenido por clientes |
| Abono | `IVA_POR_PAGAR`, o cargo a `IVA_A_FAVOR` (roles nuevos) |

Solo puede haber una por mes mientras no se anule.

## 4. Supuestos «a validar»

1. **Retenciones (K10.7-c).** El IVA retenido se toma del desglose del CFDI (impuesto `002`) con la tasa que traiga: 4 % en fletes, 10.67 % en honorarios y arrendamiento de personas físicas. Así no hay tasas fijas en el código.
   - El IVA que retienen los clientes se resta del IVA a cargo.
   - El IVA retenido a proveedores se entera aparte y no se resta.
   - El ISR retenido, por ejemplo el 10 % de honorarios, no entra en la liquidación de IVA.
   - Pendiente de confirmar con Contabilidad, junto con la DIOT y el papel de trabajo de un mes ya presentado. La ficha decía «se resta».
2. **Efectivo de caja:** se reclasifica en la liquidación de la sesión, no en cada cobro.
3. **Moneda extranjera:** el IVA pagado o cobrado se valúa con el tipo de cambio del pago. La diferencia con el pendiente, que se valuó con el tipo de cambio de la factura, queda para la revaluación cambiaria.
4. **Aplicación de anticipo (NC 07):** no mueve el IVA de la factura.
5. **Roles sin cuenta:** `IVA_TRASLADADO_COBRADO`, `IVA_POR_PAGAR` e `IVA_A_FAVOR` nacen sin cuenta y Contabilidad los mapea. Mientras tanto, los pagos y cobros con IVA quedan en la bandeja como «rol sin cuenta».
