# 21 · Apertura contable: carga de saldos iniciales (parte contable)

> M-166 · F1-CON-13 (86e3jvvmf). Plano 1.7.1, recorrido «Carga de saldos iniciales» (CON-10-01 a CON-10-05 y CON-10-R).
> La pantalla, el mapeo de columnas, la vista previa por fila y la autorización del lote son del **lote común de cargas** (M-047 y
> M-167). Este documento cubre lo que hace Contabilidad.

## 1. Qué hace

1. El lote común sube la balanza de cierre del sistema anterior y la normaliza: una fila por cuenta y, en las cuentas de control,
   una fila de detalle por cliente, proveedor o cuenta bancaria.
2. **Vista previa:** el lote llama a `ValidarBalanzaAperturaQuery`, que devuelve:
   - los errores por fila y los del archivo;
   - los totales y la diferencia;
   - si la empresa ya tiene apertura.

   No guarda nada.
3. Otra persona autoriza el lote; eso lo controla el lote común.
4. **Aplicar:** el lote llama a `AplicarSaldosInicialesCommand`. Contabilidad:
   1. valida otra vez;
   2. exige que quien autorizó sea otra persona;
   3. genera **una** póliza de apertura **autorizada**, sin borrador;
   4. guarda la marca de la apertura.

Contabilidad no lee tablas del lote: todo viaja en la consulta y en el comando (MediatR,
`Millet.Contabilidad.Application.SaldosIniciales`).

## 2. La fila
| Campo | Qué es |
|---|---|
| `Fila` | Renglón real del archivo, para los mensajes |
| `Cuenta` | Código del catálogo nuevo |
| `Cargo`, `Abono` | Saldo deudor o acreedor. No negativos, con dos decimales como máximo, uno solo por renglón |
| `TipoAuxiliar` | Vacío en el renglón de la cuenta. `Cliente`, `Proveedor` o `CuentaBancaria` en el detalle |
| `ClaveAuxiliar` | Clave del cliente o del proveedor, o número de la cuenta bancaria |
| `Sucursal` | Clave de la sucursal; si viene vacía, se usa la sucursal por omisión del lote |
| `Concepto` | Opcional. Si viene vacío: «Saldo inicial» |

## 3. Validación
**Por fila**
| Código | Regla |
|---|---|
| `AP_CUENTA_NO_EXISTE` | «Cuenta no existe en el catálogo» |
| `AP_CUENTA_INACTIVA` | La cuenta está inactiva |
| `AP_CUENTA_ACUMULATIVA` | La cuenta es de título: el saldo va en sus subcuentas |
| `AP_CARGO_Y_ABONO` | El renglón trae cargo y abono a la vez |
| `AP_IMPORTE_NEGATIVO`, `AP_IMPORTE_DECIMALES` | Importes negativos o con más de dos decimales |
| `AP_TIPO_AUXILIAR_INVALIDO`, `AP_AUXILIAR_REQUERIDO`, `AP_TIPO_AUXILIAR_REQUERIDO` | Tipo o clave de auxiliar ausentes o no válidos |
| `AP_AUXILIAR_NO_EXISTE` | La clave del auxiliar no existe |
| `AP_AUXILIAR_NO_CORRESPONDE`, `AP_AUXILIAR_EN_CUENTA_SIN_CONTROL` | El tipo de auxiliar no corresponde a la cuenta |
| `AP_SUCURSAL_REQUERIDA`, `AP_SUCURSAL_NO_EXISTE` | Falta la sucursal o no existe |
| `AP_CUENTA_REPETIDA`, `AP_AUXILIAR_REPETIDO` | La cuenta, o el auxiliar en la misma cuenta y sucursal, viene dos veces |

**Por cuenta de control**
- Las cuentas de **clientes** y **proveedores** exigen el detalle (`AP_DETALLE_REQUERIDO`). Las de deudores y acreedores lo admiten.
- Si vienen el renglón y el detalle, la suma del detalle debe igualar el renglón (`AP_DETALLE_NO_IGUALA`). Ejemplo: «El detalle de clientes no iguala el renglón (diferencia 1,000.00).».
- Las cuentas bancarias van como detalle de una cuenta sin control.

**Por archivo**
| Código | Regla |
|---|---|
| `AP_NO_CUADRA` | «No cuadra por 300.00 (cargos …, abonos …)». Cuentan los renglones y, de las cuentas que solo traen detalle, el detalle |
| `AP_FECHA_CORTE` | La fecha de corte no es el último día de un mes |
| `AP_PERIODO_NO_DISPONIBLE` | El periodo de la fecha de corte no admite pólizas |

Además, cada partida pasa por las mismas reglas que cualquier póliza (dimensiones obligatorias K10.2), y el error queda en su fila.

## 4. La póliza de apertura
- **Tipo:** el que indica el lote o, sin él, el tipo `SI`, que debe ser de Diario. **Origen:** `Apertura`, nuevo en `OrigenPoliza`.
- **Fecha:** la fecha de corte, es decir, el último día del periodo anterior al arranque. Así el mes del arranque la muestra como **saldo inicial** en la balanza y en los auxiliares, que suman los periodos anteriores.
- **Partidas:**
  - un movimiento por cuenta;
  - en las cuentas con detalle, uno por cliente, proveedor o cuenta bancaria, con `ClienteId`, `ProveedorId` o `CuentaBancariaId`;
  - las cuentas de control se validan como su auxiliar (CxC o CxP), porque la captura manual no puede tocarlas (P23).
- **Firmas:** nace **autorizada**, con quien subió el lote como capturista y quien lo autorizó como autorizador.
- **Documento de origen:** `contabilidad/apertura` con el id del lote. La llave de idempotencia es `apertura:{lote}`.

## 5. Una sola apertura
- La tabla `contabilidad.aperturas` guarda el lote, la huella SHA-256 del archivo, la fecha de corte y la póliza. Tiene un índice único por empresa.
- **Mismo archivo** (misma huella y corte): responde la póliza ya creada, con `YaAplicada = true`, y no crea otra.
- **Otro archivo** con apertura existente: 409 `AP_YA_HAY_APERTURA`, «Ya hay apertura (póliza …); use el ajuste único.». El ajuste contra el cierre definitivo es otra tarea (M-300).
- Si quien autorizó es quien subió el lote: «Debe autorizar otra persona.».

## 6. Supuestos a validar con Contabilidad
1. **Fecha:** la póliza va fechada al último día del periodo anterior al arranque (31-dic-2026 en producción; 31-oct-2026 en el ensayo).
2. **Tipo de póliza:** se sugiere dar de alta `SI` «Saldos iniciales» (Diario); las claves de tipo son de dos letras, como en SAP.
3. **Sucursal:** la balanza del sistema anterior puede no traerla. El lote fija una sucursal por omisión (propuesta: la matriz).
4. **Detalle:** se exige en clientes y proveedores. En deudores y acreedores es opcional.

## 7. Fuera de alcance
- La pantalla y el mapeo de columnas (M-167).
- El lote común (M-047).
- El ajuste único (M-300).
- La conciliación de la apertura contra CxC, CxP y Tesorería (M-301 y M-302).
