# Póliza de la nómina externa (K10.11)

> Tarea F1-CON-13 (86e3jvvmf), entrega 2 de 3 (K10.11). Rev 1.0, 7-nov-2026.

## 1. Qué resuelve

En Fase 1 la nómina se calcula fuera del ERP, con PRENÓMINA y sus herramientas. El ERP solo recibe su póliza. Con K10.11 la póliza de cada periodo entra por archivo, sin capturarla a mano:

1. La **vista previa** revisa el archivo completo y no guarda nada.
2. Si ningún renglón tiene error, la **importación** crea la póliza en **borrador**, ya cuadrada.
3. El archivo original queda adjunto a la póliza.
4. Contabilidad la autoriza por el flujo normal de C1.3, donde otra persona autoriza y se asigna el folio.

## 2. Formato (propuesta VILO, a validar con Contabilidad: D10, INS-P1-40)

Va un renglón por partida, en `.csv` (coma, punto y coma o tabulador) o en `.xlsx`. En el `.xlsx` puede haber filas de título antes del encabezado.

| Columna | Obligatoria | Alias aceptados | Regla |
|---|---|---|---|
| `cuenta` | Sí | cuenta contable, num cta, codigo cuenta | Debe existir, estar activa y ser afectable |
| `cargo` | Sí | cargos, debe | Número con dos decimales como máximo; se aceptan `1,234.56` y `$` |
| `abono` | Sí | abonos, haber | Igual que cargo. Cada renglón lleva cargo **o** abono, uno solo y distinto de cero |
| `sucursal` | Sí | clave sucursal | Clave de una sucursal existente |
| `centro` | No | centro de costo, ceco, cc | Clave de un centro de cualquier nivel; el ERP completa sus ancestros (ubicación y área) |
| `proyecto` | No | — | Texto |
| `concepto` | No | descripcion | Texto. Si viene `empleado`, se agrega «· Empleado E-001» |
| `empleado` | No | codigo empleado, codigo de nomina, num empleado | Código de nómina, sin datos personales |

El encabezado de la póliza se captura en la pantalla:

- **tipo de póliza**: el tipo de documento contable que Contabilidad defina para la nómina;
- **fecha contable**;
- **referencia del lote**, hasta 60 caracteres, por ejemplo «2026-Q22 ordinaria»;
- **concepto**, que por omisión es «Nómina {referencia}»;
- **total de control** de cargos, opcional.

## 3. Validaciones (K10.11-b)

Todos los errores se juntan y se responden juntos. Cada uno lleva su **fila** del archivo (cabecera = 1) o, si es del lote, ninguna.

| Código | Cuándo |
|---|---|
| `NOM_COLUMNA_FALTANTE` | Falta cuenta, cargo, abono o sucursal |
| `NOM_CUENTA_VACIA` / `NOM_CUENTA_NO_EXISTE` / `NOM_CUENTA_INACTIVA` / `NOM_CUENTA_NO_AFECTABLE` | Cuenta del renglón |
| `NOM_IMPORTE_INVALIDO` / `NOM_IMPORTE_NEGATIVO` / `NOM_CARGO_Y_ABONO` / `NOM_IMPORTE_DECIMALES` | Importes del renglón |
| `NOM_SUCURSAL_VACIA` / `NOM_SUCURSAL_NO_EXISTE` | Sucursal del renglón |
| `NOM_CENTRO_NO_EXISTE` | Centro del renglón |
| `NOM_NO_CUADRA` | Los cargos no son iguales a los abonos |
| `NOM_TOTAL_CONTROL` | Los cargos no son iguales al total de control |
| `NOM_LOTE_DUPLICADO` | La referencia ya tiene una póliza vigente, en borrador o autorizada |
| `NOM_POCAS_PARTIDAS` / `NOM_DEMASIADAS_PARTIDAS` | Menos de 2 renglones o más de 500 |
| Periodo cerrado o no abierto | Mismo rechazo que la captura manual |
| Reglas de dimensión K10.2 | Dimensiones obligatorias por cuenta y combinaciones válidas, con el error en su renglón |

En la importación, cualquier error responde `400` con el detalle «Fila N: …» y **no crea nada**.

## 4. Lote y correcciones

- **El lote.** La referencia identifica el lote, sin distinguir mayúsculas, acentos ni espacios. Se guarda como documento de origen de la póliza (`contabilidad` / `nomina_externa`, id estable derivado de la referencia y folio = referencia), así que no se necesita una tabla nueva.
- **Si se repite.** Mientras exista la póliza del lote, en borrador o autorizada, repetirlo responde `NOM_LOTE_DUPLICADO`.
- **Para corregir:**
  - el borrador se descarta;
  - la póliza autorizada se anula, con la reversa de C1.3 / K10.3;
  - después se vuelve a importar.
  La póliza anterior y su anulación se conservan.

## 5. Contratos

- `POST /api/v1/contabilidad/polizas/nomina/vista-previa`: devuelve `VistaPreviaNominaResponse`, con validez, renglones, cargos, abonos, cuadre, errores, partidas y columnas no reconocidas.
- `POST /api/v1/contabilidad/polizas/nomina`: devuelve `201` con `PolizaResponse` y exige `Idempotency-Key`.
- El cuerpo de ambos es el encabezado más `archivo`, con la misma forma que la importación del catálogo: CSV en base64, o columnas y filas de una hoja `.xlsx` leída en el cliente.
- **Permiso:** `contabilidad.polizas.capturar`; no hay permisos nuevos. El alcance por sucursal se verifica como en la captura manual.
- **Adjunto:** la pantalla sube el archivo original a la póliza con el endpoint de adjuntos existente (`/polizas/{id}/adjuntos`). Si la subida falla, la póliza queda creada y se avisa que hay que adjuntarlo desde su detalle.
- No hay migraciones.

## 6. Pantalla

La pantalla está en **Contabilidad › Pólizas › Importar póliza de nómina** (`/contabilidad/polizas/importar-nomina`):

- trae la plantilla CSV del formato;
- la vista previa muestra totales, cuadre, errores por fila (la fila real del archivo, también en el `.xlsx` con título) y las partidas;
- «Importar» se deshabilita, con su motivo, hasta que la vista previa salga sin errores.

## 7. Supuestos a validar con Contabilidad

1. **Formato:** el de §2, hasta recibir el layout real (D10, INS-P1-40, con Laura Cerón).
2. **Tipo de póliza:** lo elige quien importa entre los tipos de documento activos. Se sugiere que Contabilidad dé de alta uno de nómina, por ejemplo `NOM`.
3. **Estado inicial:** la póliza nace en borrador y la autoriza otra persona (C1.3), en vez de nacer autorizada.
4. **Alcance de centros:** no se exige que quien importa tenga asignados los centros del archivo, porque la nómina abarca a toda la empresa. Sí se verifica el alcance por sucursal.
5. **Conciliación contra CFDI de nómina** (dato para CON-12): la referencia del lote y el código de empleado quedan en la póliza. El cruce con los CFDI tipo N queda para el cierre.
