# 17 · Checklist de cierre y cierre anual (F1-CON-12)

Tarea: F1-CON-12 · Ejecutar cierre mensual secuencial. Reglas: R17 (conciliaciones antes de cerrar), R20 (cierre anual), P10.4
(checklist propuesto mientras Contabilidad no mande el suyo). Criterios: CA10.9, CA10.13 y CON-12-a.

## 1. Qué hace

- Cada periodo tiene un **checklist de cierre**. Cada paso es **automático** (lo verifica el ERP con los datos del módulo dueño) o
  **manual** (lo confirma una persona con nota).
- `POST /api/v1/contabilidad/periodos/{id}/cerrar` exige el checklist: si un paso **obligatorio** sigue pendiente, responde 422
  `CONTAB_CIERRE_CHECKLIST_INCOMPLETO` y el mensaje dice qué falta (CA10.9).
- Un paso pendiente deja de bloquear con una **excepción autorizada** (permiso `contabilidad.cierre.autorizar-excepcion`). Queda
  con nombre, fecha y motivo. Al cerrar se guarda la **foto del checklist** (`contabilidad.cierres_periodo`), con las excepciones y
  quién las autorizó (CON-12-a).
- **Cierre anual**: con diciembre cerrado y el periodo 13 abierto, `POST /api/v1/contabilidad/cierre/anual` genera en el 13 las
  pólizas en borrador que dejan en cero las cuentas de resultados contra resultado del ejercicio. El paso anual del checklist del
  13 exige resultados en cero (CA10.13).

## 2. Pasos con que nace el checklist

Contabilidad puede cambiar el orden, si un paso es obligatorio y si está activo, y agregar pasos manuales propios. Las claves no
cambian.

| Orden | Clave | Paso | Cómo se verifica | Dueño del dato |
|---|---|---|---|---|
| 10 | `CONCILIACION_BANCARIA` | Conciliación bancaria | Cada cuenta con movimientos en el mes tiene su conciliación del mes **cerrada** | Tesorería (T9.2) |
| 20 | `REP_CONCILIADOS` | REP conciliados | Ningún pago o cobro PPD del mes queda «PPD pagado sin REP» | Liquidación de IVA (K10.7) |
| 30 | `INGRESOS_VS_CFDI` | Ingresos contra CFDI emitidos | Ningún cobro con IVA del mes queda «Sin CFDI» | Liquidación de IVA (K10.7) |
| 40 | `NOMINA_VS_CFDI` | Nóminas contra CFDI emitidos | **Manual**: la nómina es externa (K10.11) | Contabilidad |
| 50 | `PASIVOS_VS_CFDI` | Pasivos contra CFDI recibidos | Ningún pago a proveedor con IVA del mes queda «Sin CFDI» | Liquidación de IVA (K10.7) |
| 60 | `POLIZAS_AUTOMATICAS` | Documentos con su póliza | Sin rechazos pendientes del motor registrados hasta el último día del mes | Contabilidad (C1.4) |
| 70 | `INVENTARIO_CERRADO` | Inventario cerrado | Almacén cerró el mes | Almacén |
| 80 | `DEPRECIACION` | Depreciación del mes | Corrida del mes aplicada (no aplica si aún no hay activos que deprecien) | Activos Fijos (AF11.4) |
| 90 | `REVALUACION_BANCOS_CLIENTES` | Revaluación de bancos y clientes | Revaluación de cierre corrida (no aplica sin saldos en moneda extranjera) | Contabilidad (CON-11) |
| 100 | `REVALUACION_PROVEEDORES` | Revaluación de proveedores | Revaluación de CxP corrida (no aplica sin facturas en moneda extranjera con saldo) | CxP (CXP-12) |
| 110 | `IVA_LIQUIDADO` | IVA del mes liquidado | Cuadre de IVA por documentos contra cuentas y póliza `IVA-AAAA-MM` autorizada | Contabilidad (K10.7) |
| 120 | `AUXILIARES` | Auxiliares contra cuentas de control | Clientes, proveedores y bancos cuadran al último día | Contabilidad (CON-09) |
| 130 | `POLIZAS_BORRADOR` | Pólizas del periodo autorizadas | Sin pólizas en borrador en el periodo | Contabilidad |
| 140 | `BALANZA_CUADRADA` | Balanza cuadrada | Cargos = abonos del periodo | Contabilidad |
| 10 (anual) | `CIERRE_ANUAL` | Cierre anual: resultados en cero | Cuentas de resultados del ejercicio en cero | Contabilidad |

Los pasos de otros módulos se leen con puertos de consumidor (`Contabilidad.Application.Cierre.PuertosCierre`) que implementa cada
dueño en su `Infrastructure/PublicAdapters` y cablea `Program.cs`. Contabilidad no lee tablas de otro módulo.

## 3. Supuestos a validar con Contabilidad (Laura Cerón, V13)

1. **Checklist** = el de la tabla (P10.4 sobre R17), porque Millet no ha enviado el suyo. Todos nacen obligatorios.
2. Una **cuenta bancaria sin movimientos** en el mes no exige conciliación.
3. **Rechazos del motor**: bloquean los registrados hasta el último día del mes; los posteriores solo se avisan.
4. **Nómina** se confirma a mano hasta que exista la importación de la nómina externa.
5. Las **excepciones siguen vigentes** si el periodo se reabre; se pueden retirar mientras el periodo esté abierto.
6. **Cierre anual**:
   - Cuenta de resultados = grupo de reporte «Estado de Pérdidas y Ganancias» del catálogo. Si la cuenta no tiene grupo, se usan
     los rubros 400 a 799.
   - Contrapartida por rol: `UTILIDAD_EJERCICIO` (305.01) si hubo utilidad y `PERDIDA_EJERCICIO` (305.02) si hubo pérdida.
   - Hay una contrapartida por sucursal.
   - La póliza va en el periodo 13 como manual en borrador, y la autoriza el Contador General.
   - Si después hay ajustes en el 13, se genera otra póliza por la diferencia.

## 4. Configuración

`Contabilidad:Cierre:ExigirChecklist` (por omisión `true`). Las pruebas de integración lo apagan en el host compartido (otras
suites cierran periodos sin preparar el mes) y lo encienden en `CierreChecklistHttpTests`.

## 5. Permisos

| Permiso | Uso |
|---|---|
| `contabilidad.periodo.leer` | Ver el checklist y la foto de cada cierre |
| `contabilidad.periodo.cerrar` | Confirmar pasos manuales y retirar confirmaciones |
| `contabilidad.cierre.autorizar-excepcion` | Autorizar y retirar excepciones (CON-12-a) |
| `contabilidad.cierre.configurar` | Orden, obligatorio, activo y pasos manuales |
| `contabilidad.cierre.anual` | Generar las pólizas de cierre anual |
