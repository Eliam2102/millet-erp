# ADR-0040: Fechas de calendario de negocio como `DateOnly` (refinamiento de ADR-0013)

- **Estado**: Aceptada
- **Fecha**: 2026-06-04
- **Decisores**: Eduardo Paredes (owner), Claude (backend/frontend)
- **Etiquetas**: convenciones, fechas, compras, ordenes-compra, refinamiento

> Refina (no reemplaza) [ADR-0013](./0013-tiempo-zona-horaria.md). ADR-0013
> sigue vigente para todo instante real; este ADR sólo precisa cómo se modela
> una **fecha de calendario que el usuario captura sin hora-del-día**.

## Contexto y problema

ADR-0013 fijó la convención fundacional de tiempo: columnas `timestamptz`,
`DateTimeOffset` en código, conversión a `America/Mexico_City` en presentación.
En su sección *"Inputs de fecha"* prescribió que `<DatePicker />` envíe al
backend **UTC ISO 8601** (`2026-05-02T20:30:00Z`).

En la práctica, el ERP evolucionó con dos patrones para "fechas":

- **Instantes reales** (auditoría, `created_at`, cierre, timbrado): `DateTimeOffset` / `timestamptz`. Correcto.
- **Fechas de calendario que el usuario elige sin hora** (entrega deseada de una RQ, vencimiento de una factura, fecha de movimiento de almacén, fecha del tipo de cambio): se modelaron como **`DateOnly` / `date`** — coherente con la cláusula de ADR-0013 línea 57 ("`DateOnly` para casos donde el offset no aplica conceptualmente") y con su propio precedente de tipo de cambio (ADR-0013 línea 145). El `DatePickerField` del frontend, en consecuencia, emite `YYYY-MM-DD` (no UTC ISO), y el componente de salida `<DateTimeDisplay />` detecta ese formato y lo muestra **sin conversión de zona**.

El problema concreto: la **cabecera de Orden de Compra** quedó modelada en el
lado equivocado. `FechaDocumento` y `FechaEntregaEsperada` eran `DateTimeOffset`
/ `timestamptz`, pero el `DatePickerField` les manda `YYYY-MM-DD`. Al
deserializar esa fecha-sola a `DateTimeOffset`, .NET le asigna el offset local
del **servidor**, con dos síntomas según el entorno:

- **Local (TZ México, −06:00):** Npgsql rechaza escribir un `DateTimeOffset`
  con offset ≠ 0 en una columna `timestamptz` → **500 al crear la OC**.
- **Azure (servidor UTC):** se acepta, pero se guarda medianoche-UTC; al
  renderizar en hora de México `<DateTimeDisplay />` resta 6 h → muestra el
  **día anterior** (off-by-one silencioso).

Es decir: el mismo bug se manifiesta como crash en un entorno y como dato
incorrecto callado en el otro. La causa raíz no es un caso aislado de OC, sino
la **falta de una regla explícita** que diga en qué lado del modelo va una
fecha de calendario de negocio.

## Drivers de la decisión

- Eliminar la ambigüedad "¿esta fecha es un instante o un día de calendario?".
- Que el seam de entrada (`DatePickerField` → `YYYY-MM-DD`) y el de salida
  (`<DateTimeDisplay />`, rama date-only) — **ya alineados a `DateOnly`** —
  dejen de chocar con campos `DateTimeOffset`.
- Correctitud en cualquier zona del servidor (sin depender de que dev sea
  México y prod sea UTC).
- Cortes "del día" en hora local de México, no UTC (intención original de
  ADR-0013, líneas 79-104, 135-138).

## Opciones consideradas

1. **Modelar las fechas de calendario de negocio como `DateOnly` / `date`** — *elegida*.
2. Mantener `DateTimeOffset` y normalizar a UTC en el backend antes de persistir.
3. Convertir en el submit del frontend a UTC ISO con offset local.

## Decisión

**Una fecha de calendario que el usuario captura sin hora-del-día se modela
como `DateOnly` en código y `date` en BD. Un instante real (momento exacto en
que algo ocurrió) se modela como `DateTimeOffset` y `timestamptz`.**

Reglas:

1. **Clasificación.** Si el campo responde a *"¿qué día?"* y el usuario lo
   elige en un `<DatePicker />`, es fecha de calendario → `DateOnly`/`date`.
   Si responde a *"¿en qué momento exacto?"* y lo fija el sistema/un evento,
   es instante → `DateTimeOffset`/`timestamptz`.
2. **Seam de entrada.** Para campos `DateOnly`, el `DatePickerField` emite
   `YYYY-MM-DD`. Esto **supersede la sección "Inputs de fecha" de ADR-0013
   (línea 74)** únicamente para esta clase de campo; para instantes sigue
   aplicando ADR-0013 (UTC ISO).
3. **Seam de salida.** `<DateTimeDisplay />` ya formatea `YYYY-MM-DD` sin
   conversión de zona. Ningún consumidor debe hacer `new Date(fecha)` sobre un
   campo `DateOnly` (reintroduce el off-by-one); si necesita la fecha como
   objeto, debe parsearla como fecha local.
4. **El "hoy" de una fecha de negocio se calcula en `America/Mexico_City`**,
   nunca `DateOnly.FromDateTime(UtcNow)` (que daría la fecha UTC y adelanta el
   día después de las 18:00 hora local). Punto único de verdad: el helper
   `FechaContable` (ver Notas de implementación), que ADR-0013 ya había
   especificado pero no se había implementado.
5. **Sin instante acompañante** salvo necesidad fiscal explícita. A diferencia
   del tipo de cambio (ADR-0013 línea 145, que guarda `DateOnly` +
   `vigente_desde_utc`), las fechas de cabecera de OC son días puros y **no**
   llevan una columna UTC espejo.

### Alcance de aplicación inmediata

Este ADR se aplica de entrada a la **cabecera de Orden de Compra**
(`fecha_documento`, `fecha_entrega_esperada`). El resto de fechas de calendario
ya modeladas como `DateOnly` (RQ, vencimiento CxP, movimiento Almacén, tipo de
cambio) ya cumplen. Los campos que **todavía** están como `DateTimeOffset`
siendo fechas de calendario quedan como deuda conocida (ver Consecuencias).

## Consecuencias

**Positivas**

- Correcto en cualquier zona del servidor: `date` no tiene offset, no hay 500
  ni off-by-one. El round-trip picker → BD → display es estable.
- Alinea la cabecera de OC con el patrón ya correcto del resto del ERP.
- Da casa y primer uso a `FechaContable`, el helper que ADR-0013 especificó.
- Semántica honesta: una fecha de documento es un día, no un instante.

**Negativas**

- Queda **deuda conocida** de campos `DateTimeOffset` que conceptualmente son
  fechas de calendario y reproducirían el mismo bug en cuanto se les conecte un
  `<DatePicker />`. Se remedian bajo esta convención en PRs follow-up:
  - Compras: `FechaEntregaLinea` (línea de OC).
  - CxP: `FechaDocumento` / `FechaContabilizacion` (captura de factura),
    `FechaCfdi` (anticipos, notas de crédito).
- Persisten en el código varios `DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)`
  que calculan el "hoy" en UTC (Series, Evaluador de matriz, Almacén, REPP).
  Bajo la regla 4 son incorrectos; se migran a `FechaContable.HoyLocal` en
  follow-up. **No** se tocan en el PR que introduce este ADR.

## Descartadas

**Normalizar a UTC en el backend** (Opción 2): es el espejismo barato. Un
`.ToUniversalTime()` "pelón" depende de la zona del servidor — en Azure-UTC
guarda medianoche-UTC y reintroduce el off-by-one silencioso al renderizar en
México. Para ser correcto habría que construir el instante como medianoche-local
explícito, y aun así el tipo seguiría mintiendo ("instante" para algo que es un
día).

**Convertir en el submit del frontend** (Opción 3): sólo taparía la cabecera de
OC (2 campos), dejaría vivo el desajuste de tipo y los demás campos expuestos, y
exige mandar el offset local (`-06:00`, no `Z`) para no causar off-by-one — frágil.

## Notas de implementación

**Helper `FechaContable`** (implementa lo que ADR-0013 especificó):

- Ubicación: `backend/src/SharedKernel/Application/FechaContable.cs`.
- Constante `ZonaOperacion = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City")`
  (IANA; .NET 6+ lo resuelve cross-platform vía ICU). México no tiene horario
  de verano desde 2022 (ADR-0013 línea 118).
- `DateOnly HoyLocal(DateTimeOffset instanteUtc)` — día calendario local de
  México del instante dado.
- Extensión `DateOnly HoyLocal(this IClock clock)` — azúcar sobre
  `clock.UtcNow`, para que los handlers deriven "hoy" determinísticamente desde
  el reloj inyectado (testeable con `TestClock`/`FakeClock`). Cumple
  `BannedApiAnalyzers` (no usa `DateTime.UtcNow`).
- Los bounds de rango UTC (`GetDayBoundsUtc`, mes, año) que ADR-0013 esbozó se
  agregarán a esta misma clase cuando un reporte los demande.

**Migración de la cabecera de OC**: `AlterColumn` de `fecha_documento` y
`fecha_entrega_esperada` de `timestamptz` a `date` con
`USING (... AT TIME ZONE 'America/Mexico_City')::date`, para que las filas
existentes preserven el día visible en hora local.

**Frontend**: el default de `fechaDocumento` se calcula con
`formatInTimeZone(now, 'America/Mexico_City', 'yyyy-MM-dd')` (regla 4: "hoy"
local, no `toISOString().split('T')[0]` que sería UTC).
