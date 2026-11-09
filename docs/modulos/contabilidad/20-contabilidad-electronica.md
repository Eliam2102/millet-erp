# Contabilidad electrónica del SAT (K10.8)

> Tarea F1-CON-13 (86e3jvvmf), entrega 1 de 3 (K10.8). Rev 1.0, 7-nov-2026.

## 1. Qué resuelve

Millet manda cada mes al SAT su catálogo de cuentas con código agrupador y su balanza de comprobación. Cuando el SAT las pide, también manda las pólizas del periodo. Hasta ahora esos archivos los generaba SAP; con K10.8 los genera el ERP en el formato del anexo 24, **versión 1.3**.

| Archivo | Sufijo | Cuándo | Contenido |
|---|---|---|---|
| Catálogo de cuentas | `CT` | Al cambiar el catálogo | Cuentas activas con código agrupador, nivel, naturaleza y cuenta de mayor (`SubCtaDe`) |
| Balanza de comprobación | `BN` normal, `BC` complementaria | Mensual; el mes 13 es la balanza de cierre | Saldo inicial, cargos, abonos y saldo final de cada cuenta |
| Pólizas del periodo | `PL` | Solo cuando el SAT las pide | Transacciones de cada póliza y el CFDI de origen (`CompNal`) |

El nombre del archivo es el que pide el SAT: RFC + año + mes + sufijo, por ejemplo `AAA010101AAA202701BN`. Por omisión se descarga el `.zip` con el XML adentro, que es lo que se sube al buzón tributario. El ERP **no sella** los archivos: Millet los firma con su e.firma al enviarlos.

## 2. Revisión previa (K10.8-a)

`GET /api/v1/contabilidad/contabilidad-electronica/revision?anio=&mes=` regresa lo que impide generar o conviene revisar:

- **RFC de la empresa.** Si falta o no tiene el formato del SAT, no se genera ningún archivo.
- **Cuentas pendientes.** Se listan las cuentas activas que no tienen código agrupador, cuyo código no está en el catálogo `c_CodAgrup` del SAT, o que no tienen naturaleza. Mientras haya alguna, el catálogo no se genera (`CONTAB_CE_CATALOGO_INCOMPLETO`). La balanza y las pólizas sí se pueden generar, porque no usan el código agrupador.
- **Estado del periodo.** Si el periodo no está cerrado, se avisa que la balanza y las pólizas pueden cambiar.
- **CFDI de las pólizas.** Se avisa de cada póliza con UUID cuyos datos (RFC y total) no se encontraron. Esa póliza sale sin comprobante.

El código agrupador ya se captura en la cuenta (`CuentaContable.CodigoAgrupador`) y el importador del catálogo lo lee de la columna «Código agrupador SAT».

## 3. Reglas de cada archivo

### 3.1 Catálogo (`CT`)

- Entran las cuentas **activas**. Los rubros de reporte no son cuentas y no entran.
- `Nivel` es el nivel de la cuenta. `SubCtaDe` es el código de la cuenta padre; las de nivel 1 no lo llevan.
- `Natur` es `D` para deudora y `A` para acreedora.
- `Desc` es el nombre de la cuenta, recortado a 400 caracteres.

### 3.2 Balanza (`BN` / `BC`)

- Se usan los mismos movimientos que la balanza de C1.7: pólizas autorizadas y anuladas con su anulación; los borradores no cuentan.
- El saldo inicial es todo lo anterior al mes, y los cargos y abonos son los del mes.
- **Cada cuenta acumula a sus subcuentas**, como en el catálogo: la de mayor lleva la suma de sus hijas.
- **Los saldos van según la naturaleza.** En una deudora el saldo es cargos − abonos; en una acreedora, abonos − cargos.
- Solo salen las cuentas con saldo o con movimientos.
- La complementaria lleva `TipoEnvio="C"` y la fecha de la última modificación (`FechaModBal`), que es obligatoria.
- El mes 13 es la balanza de cierre: lleva lo anterior al periodo de ajustes como saldo inicial y los movimientos del periodo 13.

### 3.3 Pólizas (`PL`)

- Lleva el tipo de solicitud:
  - `AF` (acto de fiscalización) y `FC` (fiscalización compulsa) llevan número de orden, con la forma `ABC1234567/12`;
  - `DE` (devolución) y `CO` (compensación) llevan número de trámite, con la forma `AB123456789012`.
- `NumUnIdenPol` es el folio de la póliza, por ejemplo `FP-000123`.
- Cada partida es una `Transaccion`, con `DesCta` igual al nombre de la cuenta. Si la partida no tiene concepto, se usa el de la póliza.
- Los textos se recortan al largo del SAT: 300 caracteres el concepto de la póliza, 200 el de la transacción y 100 `DesCta`.
- **CFDI (`CompNal`).** Si la póliza tiene UUID de origen, el `CompNal` va en cada transacción, con estos datos:
  - el RFC de la contraparte: el emisor si el CFDI es recibido (buzón de CxP) o el receptor si es emitido (Facturación);
  - el total;
  - la moneda y el tipo de cambio, si la moneda no es MXN.
- Los datos del CFDI se leen por puertos públicos, sin tocar las tablas de otros módulos (ver §4).

### 3.4 Validación

Antes de entregar el archivo, cada XML se valida contra los **esquemas oficiales del SAT**. Están embebidos en `Millet.Contabilidad` (`Application/ContabilidadElectronica/Esquemas/`) y se descargaron de `http://www.sat.gob.mx/esquemas/ContabilidadE/1_3/`. Así la validación no depende de la red. Si un XML no cumple el esquema, se responde `CONTAB_CE_XML_INVALIDO` con los primeros errores.

## 4. Contratos

| Pieza | Tipo | Dueño | Uso |
|---|---|---|---|
| `IEmpresaContabilidadReadPort.RfcAsync` | Puerto consumidor (Contabilidad) | Compartido (`EmpresaContabilidadReadAdapter`) | RFC de la empresa |
| `IComprobanteFiscalContabilidadReadPort.ObtenerAsync` | Puerto consumidor (Contabilidad) | `Millet.Api` (`ComprobanteFiscalContabilidadReadAdapter`) | Datos del CFDI por UUID |
| `ICfdiRecibidoPublicReadPort.ListarPorUuidsAsync` | Puerto público de CxP (método nuevo, aditivo) | CxP | CFDI recibidos por UUID; `CfdiRecibidoPublico` suma `TipoCambio`, opcional |
| `IFacturacionCfdiReadPort.ObtenerPorUuidsAsync` | Puerto público de Facturación (método nuevo, aditivo) | Facturación | CFDI emitidos por UUID (RFC del receptor) |

Endpoints, con el permiso `contabilidad.polizas.leer` (el mismo de los reportes de C1.7; no hay permisos nuevos):

- `GET /api/v1/contabilidad/contabilidad-electronica/revision?anio=&mes=`
- `GET /api/v1/contabilidad/contabilidad-electronica/{catalogo|balanza|polizas}?anio=&mes=&tipoEnvio=N|C&fechaModificacion=aaaa-mm-dd&solicitud=AF|FC|DE|CO&numOrden=&numTramite=&formato=zip|xml`

No hay migraciones: todo es de solo lectura.

## 5. Pantalla

La pantalla está en **Contabilidad › Reportes › Contabilidad electrónica** (`/contabilidad/reportes/contabilidad-electronica`). Abre con el mes anterior.

- La casilla «Periodo 13» sirve para la balanza de cierre.
- La tarjeta de revisión muestra el RFC, el estado del periodo, las pólizas del mes y los avisos.
- Las cuentas pendientes se listan con enlace a su ficha del catálogo.
- Cada archivo tiene su botón. Si el botón está deshabilitado, el motivo aparece en el `title` y debajo del botón.

## 6. Supuestos a validar con Contabilidad

1. **Versión 1.3 del anexo 24**, que es la vigente.
2. **Balanza:** van las cuentas de todos los niveles que tengan saldo o movimientos, con los saldos según la naturaleza de la cuenta.
3. **Balanza complementaria:** se genera cuando se pide, con la fecha de la última modificación que se capture. Por omisión se usa la fecha de hoy.
4. **`CompNal` en cada transacción** de la póliza que tiene CFDI de origen. Hoy solo las pólizas de facturas de proveedor traen UUID.
5. **Pólizas del periodo 13:** el SAT solo acepta los meses 1 a 12 en las pólizas, así que las del periodo de ajustes no salen en el archivo `PL`.
6. **Código agrupador (D13):** el catálogo de Contabilidad del 5-nov ya trae la columna. Falta confirmar que el catálogo cargado en el servidor sea esa versión.
