# 12 · Tipo de Cambio para A+W (flujo 14 / I13.2 · 86e3kdvfz)

> **Propuesta para visto bueno de TI de Millet (no ejecutar sin acuerdo previo).**  
> Script DDL SQL Server: `docs/operacion/aw-integracion-scripts/18_create_erp_tipo_cambio.sql`  
> Esquema Demo PostgreSQL: `tools/aw-origen-demo/schema.sql` (`dbo.erp_tipo_cambio` y `sysadm.ka_waehrungen`)

---

## 1. Propósito y Frontera de Integración

Al apagarse SAP (corte del 1 de enero), la tarea programada que hoy descarga tipos de cambio de Banxico y los escribe en SAP y A+W dejará de actualizar SAP. Para asegurar que A+W y el ERP operen con la misma paridad cambiaria oficial y auditada:

1. El ERP calcula y centraliza el Tipo de Cambio oficial para **USD, EUR y CAD** mediante la regla **K10.6** (escalera Banxico FIX → DOF → fecha de aplicación; días inhábiles repiten el último valor bancario hábil).
2. El ERP **publica** los tipos de cambio en la tabla puente `dbo.erp_tipo_cambio` de la base de datos de integración `MILLET_INTEGRACION`.
3. La tarea programada de TI de Millet **lee** de `dbo.erp_tipo_cambio` y aplica los valores en A+W (`SYSADM.KA_WAEHRUNGEN`), reportando el resultado en las columnas de acuse de la misma fila.

### Anti-Scope y Separación de Responsabilidades

- **El ERP no escribe directamente en tablas internas de A+W** (`MILMAIN`, esquemas `SYSADM` o similares). Lo prohíben el anti-scope del módulo y la adenda CE-04.
- El usuario del ERP (`millet_erp_integracion`) solo tiene permisos de `INSERT` y `SELECT` en `dbo.erp_tipo_cambio`. Tiene expresamente denegados `UPDATE` y `DELETE`.
- La tarea de TI de Millet tiene permisos de `SELECT` y `UPDATE` restringido a las cuatro columnas de acuse (`aplicada_at`, `resultado`, `motivo`, `intentos`).

---

## 2. Contrato de Datos: `dbo.erp_tipo_cambio`

La tabla reside en `MILLET_INTEGRACION` (producción) y `MILLET_INTEGRACION_DEV` (desarrollo/pruebas).

| Columna | Tipo SQL Server | Tipo PostgreSQL Demo | Nulo | Propietario | Descripción |
|---|---|---|---|---|---|
| `moneda` | `nvarchar(8)` | `varchar(8)` | NO | ERP | Valor propio de A+W (`KA_WAEHRUNGEN.WAEHRUNG`): `USD`, `Euro` o `CAN`. No es ISO ni `FREMD_KEY`. El ERP traduce desde ISO con `IntegracionesAw:MapeoMonedaAw` (`USD`→`USD`, `EUR`→`Euro`, `CAD`→`CAN`), el mismo mapa de `aw_retorno`. |
| `fecha_aplicacion` | `date` | `date` | NO | ERP | Día en que rige el tipo de cambio según la escalera K10.6 (incluye inhábiles). |
| `version` | `int` | `integer` | NO | ERP | Versión de la paridad para esa moneda y fecha (comienza en 1; se incrementa si hay corrección). |
| `valor` | `decimal(18, 6)` | `numeric(18, 6)` | NO | ERP | Pesos mexicanos (MXN) por unidad de moneda extranjera (4 decimales efectivos, `valor > 0`). |
| `fecha_fix` | `date` | `date` | SÍ | ERP | Fecha del tipo de cambio FIX de Banxico que originó la paridad (NULL si fue captura manual). |
| `origen` | `nvarchar(10)` | `varchar(10)` | NO | ERP | Origen del valor: `'Banxico'` o `'Manual'`. |
| `creada_at` | `datetimeoffset(3)` | `timestamptz` | NO | ERP | Timestamp UTC de inserción en la tabla puente (`DEFAULT SYSDATETIMEOFFSET()` / `now()`). |
| `aplicada_at` | `datetimeoffset(3)` | `timestamptz` | SÍ | TI | Timestamp en que la tarea de TI procesó el intento de aplicación en A+W. |
| `resultado` | `tinyint` | `smallint` | SÍ | TI | Estado del procesamiento: `NULL` (pendiente), `1` (aplicada), `2` (rechazada), `3` (omitida). |
| `motivo` | `nvarchar(500)` | `varchar(500)` | SÍ | TI | Mensaje de error o justificación en caso de rechazo u omisión. |
| `intentos` | `int` | `integer` | NO | TI | Conteo de intentos de aplicación realizados por TI (`DEFAULT 0`). |

### Llave Primaria y Restricciones

- **Llave Primaria:** `PRIMARY KEY (moneda, fecha_aplicacion, version)`.
- **Restricciones de Integridad:**
  - `moneda IN ('USD', 'Euro', 'CAN')`
  - `version > 0`
  - `valor > 0`
  - `origen IN ('Banxico', 'Manual')`
  - `resultado IS NULL OR resultado IN (1, 2, 3)`
  - `intentos >= 0`

### Semántica de Estados (`resultado`)

- `NULL` (**Pendiente**): Fila publicada por el ERP a la espera de ser procesada por la tarea de TI.
- `1` (**Aplicada**): El valor fue escrito exitosamente en A+W (`SYSADM.KA_WAEHRUNGEN`).
- `2` (**Rechazada**): Error al aplicar en A+W (ej. bloqueo de tabla, error de conexión, tipo de dato inválido). La tarea de TI incrementa `intentos` y reintenta hasta 3 veces.
- `3` (**Omitida**): Versión superada. Si antes de aplicar la versión $N$ el ERP publicó una versión $N+1$, la versión $N$ no se aplica y se marca como 3 con motivo `'Superada por versión mayor'`.

---

## 3. Reglas de Publicación del ERP

### 3.1 Cálculo de Fechas y Escalera (K10.6)

1. **Horario Banxico:** El Banco de México publica el FIX alrededor de las 12:00 hrs de cada día hábil bancario.
2. **Anticipación:** La paridad que rige las operaciones de mañana se calcula a partir del FIX publicado hoy. En consecuencia, **el tipo de cambio de mañana queda disponible hoy después de las 12:00 hrs**.
3. **Fines de semana y días festivos:** La corrida del viernes a las 12:00 calcula y publica por adelantado los registros de **sábado, domingo y lunes** (o martes si el lunes es festivo bancario), repitiendo el valor oficial conforme al calendario bancario.
4. **Respaldo ante ausencia de FIX:** Si a las 12:00 Banxico no ha publicado el FIX y no existe captura manual de Contabilidad, el ERP **no inventa valores**. A+W conservará su último valor y a las 18:00 se disparará una alerta (I13.2-a).

### 3.2 Idempotencia y Versionamiento

El servicio publicador del ERP (`AwTipoCambioWorker`) opera en ciclos periódicos (cada 60 minutos):

- Consulta en `compartido.tipos_cambio` el rango desde `hoy - 7 días` hasta la última fecha conocida con paridad.
- Para cada moneda (`USD`, `EUR`, `CAD` en el ERP; se guardan como `USD`, `Euro`, `CAN`) y fecha:
  - Consulta la fila existente en `dbo.erp_tipo_cambio` con mayor `version`.
  - **Si no existe registro:** Inserta una nueva fila con `version = 1`.
  - **Si existe registro con el mismo `valor`:** No realiza acción alguna (no-op idempotente).
  - **Si existe registro pero el `valor` difiere** (ej. corrección de captura manual o rectificación oficial): Inserta una nueva fila con `version = version_actual + 1`.
- **Inmutabilidad estricta:** El ERP nunca modifica ni borra registros existentes. Toda corrección es una adición cronológica auditable.

---

## 4. Reglas de Lectura y Aplicación para la Tarea de TI de Millet

### 4.1 Horario de Ejecución

Se sugiere programar la tarea de TI para correr a las **00:05 hrs** de cada día (o en su pasada previa a la apertura de planta a las **08:00 hrs**), y posteriormente cada 15 a 30 minutos para atender posibles correcciones intradía.

### 4.2 Algoritmo de Aplicación

Para cada una de las monedas (`USD`, `Euro`, `CAN`):

1. **Seleccionar fila a aplicar:**  
   Obtener de `dbo.erp_tipo_cambio` la fila cuya `fecha_aplicacion` sea igual a la fecha del día en curso (hora centro de México) con la versión máxima (`MAX(version)`) donde:
   - `resultado IS NULL` o `(resultado = 2 AND intentos < 3)`.
2. **Si no existe fila para hoy:**  
   **No realizar modificaciones en A+W.** A+W conservará el factor de tipo de cambio vigente del día anterior.
3. **Mapeo de moneda a A+W:**  
   Localizar la moneda en `SYSADM.KA_WAEHRUNGEN` con `WAEHRUNG = @moneda`, donde `@moneda` es el valor de la columna `moneda` tal cual (`USD`, `Euro`, `CAN`). `FREMD_KEY` no es la moneda y no se usa. Hay un solo valor vigente por moneda: `VALUTAKURS` y `VALUTADATUM`.
4. **Actualización en A+W:**  
   Actualizar el factor de cambio y la fecha:
   - `SYSADM.KA_WAEHRUNGEN.VALUTAKURS = @valor`
   - `SYSADM.KA_WAEHRUNGEN.VALUTADATUM = @fecha_aplicacion`
5. **Escribir Acuse:**
   - **Éxito:**  
     Actualizar la fila aplicada con `aplicada_at = SYSDATETIMEOFFSET()`, `resultado = 1`, `motivo = NULL`.
   - **Fallo:**  
     Actualizar la fila con `aplicada_at = SYSDATETIMEOFFSET()`, `resultado = 2`, `motivo = '<detalle del error>'`, `intentos = intentos + 1`.
6. **Descartar versiones obsoletas:**  
   Si existían versiones menores pendientes para la misma moneda y fecha (`version < @version AND resultado IS NULL`), marcarlas con `resultado = 3` y `motivo = 'Superada por versión mayor'`.

---

## 5. Supervisión y Alertas Automáticas

El ERP cuenta con tres mecanismos de alerta integrados en `AwTipoCambioWorker`:

1. **Alerta 1 · TC de mañana sin publicar (I13.2-a):**  
   Si a las 18:00 hrs el ERP no ha publicado el tipo de cambio de la fecha de mañana (por ejemplo, porque Banxico no emitió el FIX y no hubo captura manual en el ERP), se emite una alerta diaria al equipo de Contabilidad y TI.
2. **Alerta 2 · A+W no aplicó el TC de hoy (I13.2-e):**  
   Si a las 09:00 hrs la fila correspondiente a la fecha de hoy continúa con `resultado IS NULL` o acumula `intentos >= 3` con `resultado = 2`, se notifica al equipo de soporte de TI.
3. **Alerta 3 · Desfase ERP vs A+W (Control de CA13.9):**  
   El ERP lee periódicamente el valor vigente en `SYSADM.KA_WAEHRUNGEN` (cruce por `WAEHRUNG`) (permiso de `SELECT` ya otorgado al login `millet_erp_integracion`). Si el factor vigente en A+W no coincide con el valor publicado para hoy después de la hora límite, se emite una alerta de discrepancia cambiaria.

> **Control de Frecuencia:** Cada alerta cuenta con un control de supresión para emitirse como máximo una sola vez por día calendario.

---

## 6. Seguridad y Permisos SQL

### En `MILLET_INTEGRACION` (Base de datos de integración)

```sql
-- Usuario del ERP (script 04_create_login.sql)
GRANT SELECT, INSERT ON dbo.erp_tipo_cambio TO millet_erp_integracion;
DENY  UPDATE, DELETE ON dbo.erp_tipo_cambio TO millet_erp_integracion;

-- Cuenta de servicio de TI (tarea programada de aplicación)
GRANT SELECT ON dbo.erp_tipo_cambio TO <cuenta_servicio_ti>;
GRANT UPDATE (aplicada_at, resultado, motivo, intentos) ON dbo.erp_tipo_cambio TO <cuenta_servicio_ti>;
```

### En bases de datos de A+W (`MILMAIN`)

```sql
-- Permiso de sólo lectura para que el ERP verifique el valor vigente (otorgado previamente en 04_create_login.sql):
GRANT SELECT ON SYSADM.KA_WAEHRUNGEN TO millet_erp_integracion;
```

---

## 7. Consultas Útiles de Diagnóstico y Soporte

### 7.1 Filas pendientes de aplicar para el día de hoy
```sql
SELECT *
FROM dbo.erp_tipo_cambio
WHERE fecha_aplicacion = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Central Standard Time (Mexico)' AS date)
  AND resultado IS NULL
ORDER BY moneda, [version] DESC;
```

### 7.2 Filas rechazadas con 3 o más intentos
```sql
SELECT *
FROM dbo.erp_tipo_cambio
WHERE resultado = 2
  AND intentos >= 3
ORDER BY fecha_aplicacion DESC, moneda;
```

### 7.3 Auditoría de correcciones cambiarias (múltiples versiones)
```sql
SELECT moneda, fecha_aplicacion, COUNT(*) AS total_versiones, MAX(valor) - MIN(valor) AS variacion_mxn
FROM dbo.erp_tipo_cambio
GROUP BY moneda, fecha_aplicacion
HAVING COUNT(*) > 1
ORDER BY fecha_aplicacion DESC;
```
