# ADR-0013: Tiempo, zona horaria y manejo de fechas

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: convenciones, fechas, fiscal, fundación

## Contexto y problema

El ERP opera para empresas en México y todas las decisiones temporales
relevantes (cierres contables, cortes de cobranza, periodos fiscales,
reportes diarios, fechas de emisión de CFDI) ocurren en **hora local de la
Ciudad de México**. Sin embargo, la BD y los servicios técnicos viven en un
mundo donde el estándar es UTC.

Si no se decide explícitamente cómo se almacenan, transportan y presentan
las fechas, terminamos con bugs sutiles imposibles de depurar: un CFDI
emitido el día 31 a las 23:30 hora local aparece registrado el día 1 del mes
siguiente; un cierre mensual deja fuera transacciones que ocurrieron en su
periodo; un reporte "movimientos del día" no muestra los del último par de
horas porque ya cambió la fecha en UTC.

Necesitamos convenciones uniformes desde el día 1, aplicadas
automáticamente por el framework cuando sea posible, y documentadas para los
casos donde el dev tiene que pensarlas.

## Drivers de la decisión

- Cero ambigüedad: cada `DateTime` en el código tiene un significado claro
- Cumplimiento fiscal: cortes y periodos por hora local de Ciudad de México
- Testabilidad: el tiempo no debe venir de "ahora real" sino inyectado
- Compatibilidad con eventos externos (SAT, PAC) que ya vienen con timestamp
- Consistencia frontend-backend: el usuario nunca ve UTC

## Opciones consideradas

1. UTC en BD, conversión a `America/Mexico_City` en presentación, `DateTimeOffset` en código
2. Hora local en BD, sin conversiones (más simple pero rompible)
3. UTC en BD, presentar UTC al usuario (técnicamente correcto, terriblemente mala UX)
4. `DateTime` con `Kind = Utc` en código (sin offset explícito)

## Decisión

Se adopta la **opción 1**: UTC en BD, `DateTimeOffset` en código, conversión
a `America/Mexico_City` en presentación.

### Almacenamiento

- Todas las columnas temporales son **`timestamptz`** (PostgreSQL guarda internamente en UTC, sin importar el offset enviado)
- En la BD nunca hay valores en hora local

### Tipos en .NET

- **`DateTimeOffset`** para todo dato temporal en código de dominio. Razón: elimina la ambigüedad de `DateTime` (que puede ser `Local`, `Utc` o `Unspecified` y dependiendo del kind se serializa distinto)
- **Prohibido `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`** en código de dominio. Estos son fuentes de no-determinismo
- **Obligatorio inyectar `IClock`** vía DI: interfaz con `DateTimeOffset UtcNow { get; }`. Implementación de producción usa `DateTimeOffset.UtcNow`; tests usan `FixedClock` o `TestClock` controlable
- `DateOnly` y `TimeOnly` se permiten para casos específicos (fecha de nacimiento, hora de cita) donde el offset no aplica conceptualmente

### Presentación al usuario

- El frontend **siempre** convierte UTC → `America/Mexico_City`
- Los formatos estándar:
  - Fecha-hora: `dd/MM/yyyy HH:mm` (ej. `02/05/2026 14:30`)
  - Fecha: `dd/MM/yyyy` (ej. `02/05/2026`)
  - Fecha larga: `2 de mayo de 2026`
  - Fecha-hora con segundos (logs, auditoría): `dd/MM/yyyy HH:mm:ss`
- Se usa la librería `date-fns-tz` (o equivalente) en `frontend/src/lib/datetime.ts` como único punto de conversión y formato
- Componentes reutilizables: `<DateTimeDisplay value={utcString} />`, `<DateDisplay />`, `<RelativeTimeDisplay />` ("hace 5 minutos")
- **Sin excepciones**: ni admin, ni reportes técnicos, ni logs en UI muestran UTC. Si un dev necesita ver UTC para depurar, abre las DevTools y mira la respuesta cruda del API

### Inputs de fecha

- `<DatePicker />` y `<DateTimePicker />` interpretan la entrada del usuario como hora local de `America/Mexico_City`
- Al enviar al backend, se convierte a UTC ISO 8601: `2026-05-02T20:30:00Z`
- El backend recibe siempre UTC; el `DateTimeOffset` deserializado conserva el offset enviado pero la lógica trabaja con `UtcDateTime`

### Cortes y periodos por hora local

Esta es la parte sutil: las queries que responden a "movimientos del día",
"cierre de mes", "reporte semanal" deben filtrar por **rango UTC equivalente
al rango local**, no por rango UTC simple.

Helper en backend:

```csharp
public static class FechaContable
{
    private static readonly TimeZoneInfo CdmxTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    public static (DateTimeOffset startUtc, DateTimeOffset endUtc)
        GetDayBoundsUtc(DateOnly localDate)
    {
        var localStart = new DateTime(localDate, TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd   = new DateTime(localDate.AddDays(1), TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc   = TimeZoneInfo.ConvertTimeToUtc(localStart, CdmxTz);
        var endUtc     = TimeZoneInfo.ConvertTimeToUtc(localEnd,   CdmxTz);
        return (startUtc, endUtc);
    }

    // Equivalentes para mes, semana, año fiscal
    public static (DateTimeOffset, DateTimeOffset) GetMonthBoundsUtc(int year, int month) { ... }
    public static (DateTimeOffset, DateTimeOffset) GetYearBoundsUtc(int year) { ... }
}
```

Queries que reportan "cierre del mes de mayo 2026" hacen:

```csharp
var (start, end) = FechaContable.GetMonthBoundsUtc(2026, 5);
var movimientos = ctx.Movimientos
    .Where(m => m.Fecha >= start && m.Fecha < end)
    .ToList();
```

### Horario de verano (DST)

México **eliminó el horario de verano en 2022**. La zona `America/Mexico_City`
en `tzdata` ya refleja eso: a partir de octubre 2022 no hay cambios de
horario. Pero los timestamps **históricos** anteriores a 2022 sí tuvieron
DST y la conversión correcta requiere consultar la regla histórica de
`tzdata`.

`TimeZoneInfo.ConvertTimeToUtc` y las librerías estándar lo manejan
automáticamente siempre que se confíe en `tzdata`. Implicaciones:
- En desarrollo, asegurar que el sistema operativo o `Microsoft.NET.Sdk`
  incluyen tzdata actualizado
- En producción (App Service Linux), incluir paquete `Microsoft.TimezoneRules` o
  equivalente para garantizar tzdata up-to-date
- En migraciones de datos históricos desde SAP, las fechas anteriores a 2022
  pueden necesitar conversión consciente del DST de su época

### Periodos fiscales y contables

- **Día contable**: 00:00:00.000 hasta 23:59:59.999 en `America/Mexico_City`
- **Mes contable**: mes calendario de `America/Mexico_City`
- **Año fiscal**: enero-diciembre, hora local
- **Cierre mensual**: el job se ejecuta cuando termina el último segundo del último día del mes en hora local. Se programa con cron consciente de zona horaria, no con UTC fijo
- **Periodos editables**: un periodo se considera "abierto" hasta su cierre formal; después es read-only (lock duro a nivel de periodo, ver ADR-0012)

### Eventos externos

- Timbrados del SAT/PAC: el timestamp llega con offset (típicamente `-06:00`); se almacena en UTC tal cual lo manda el PAC y se confía en su valor para efectos fiscales
- Webhooks de proveedores: cualquier timestamp se convierte a UTC al ingresar al sistema; si el remitente no especifica zona, se asume UTC y se documenta como suposición
- DOF (tipos de cambio, ADR-0014): el DOF publica con fecha del día hábil; se almacena la fecha como `DateOnly` con campo `vigente_desde_utc` calculado a las 00:00 hora local de México

### Auditoría

- `audit_log.timestamp` (ADR-0008) es UTC en `timestamptz`
- Queries de auditoría tipo "qué hizo el usuario X el 30 de abril" convierten el rango local del día a UTC primero usando `FechaContable.GetDayBoundsUtc`
- La UI de auditoría muestra siempre hora local

## Consecuencias

**Positivas**
- Cero ambigüedad: cada `DateTimeOffset` tiene significado claro
- BD agnóstica: si en el futuro hay operaciones en otra zona horaria, el código se adapta sin migración de datos
- Testabilidad fuerte vía `IClock`: tests deterministas
- Cumplimiento fiscal correcto: cortes contables alineados con la realidad operativa de México
- Frontend desacoplado: si en el futuro se agregan empresas en otra zona horaria, solo cambia la conversión, no los datos

**Negativas**
- Disciplina obligatoria: usar `DateTimeOffset` y `IClock` siempre; un dev distraído que escribe `DateTime.Now` introduce un bug sutil. Mitigado por linter/analyzer que prohíbe `DateTime.Now/UtcNow` fuera de la implementación de `IClock`
- Helpers de rango son obligatorios: queries por "el día X" no pueden hacerse con `WHERE DATE(timestamp) = ...` (porque interpretaría UTC). Hay que usar `GetDayBoundsUtc`. Es la trampa más común
- Tzdata debe estar actualizado en producción: si el SO no tiene la regla post-2022 de México, las conversiones serán incorrectas. Mitigado documentando dependencia explícitamente

## Descartadas

**Hora local en BD**. Tentador por simplicidad, pero rompible: cualquier
operación que cruce zonas horarias (un job en Azure que vive en UTC, un
backup que se restaura en otro server, una integración con un sistema en
otro país) introduce errores. Es una decisión que se ve fácil ahora y se
paga durante años.

**Mostrar UTC al usuario**. Inaceptable como UX. Un CFDI emitido a las
14:30 hora local NO debe mostrarse como `20:30 UTC` en ninguna parte del
sistema.

**`DateTime` con Kind=Utc en código**. Funciona pero la ambigüedad regresa
en serialización: un `DateTime.Kind = Utc` y un `DateTime.Kind = Unspecified`
con el mismo valor numérico se serializan distinto en JSON. `DateTimeOffset`
elimina ese problema porque siempre incluye el offset.

## Notas de implementación

**Backend**
- Crear interfaz `IClock` y registrar `SystemClock` (producción) y `TestClock` (testing)
- Configurar Npgsql para mapear `timestamptz` → `DateTimeOffset` (es el default desde Npgsql 6+)
- Crear `FechaContable` static class con helpers de rango (día, mes, año, semana)
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` con `BannedSymbols.txt` que prohíbe `M:System.DateTime.get_Now`, `M:System.DateTime.get_UtcNow`, `M:System.DateTimeOffset.get_Now`. Mensaje custom en cada línea redirige a `IClock` y referencia esta ADR. Falla el build si encuentra usos
- Excepción explícita: `SystemClock.cs` declara `[SuppressMessage("ApiDesign", "RS0030", Justification = "...")]` a nivel del método que devuelve la hora actual; ningún otro archivo del proyecto debe usar esta supresión (revisable en code review)

**Frontend**
- Crear `frontend/src/lib/datetime.ts` con funciones `formatDateTime(utc)`, `formatDate(utc)`, `formatDateLong(utc)`, `parseLocalToUtc(localString)`, `getRelative(utc)`
- Componentes `<DateTimeDisplay />`, `<DateDisplay />`, `<RelativeTimeDisplay />`
- `<DatePicker />` y `<DateTimePicker />` envuelven la librería de UI elegida y aplican la conversión de zona automáticamente
- Constante única `TIMEZONE = "America/Mexico_City"` exportada desde `lib/datetime.ts`

**Infraestructura**
- App Service: configurar variable de entorno `TZ=America/Mexico_City` solo si afecta logs del SO; el código no debe depender de la zona del SO
- Imagen base de Docker (si aplica) que incluya tzdata reciente
- CI: test que valida que `tzdata` reconoce la regla post-2022 de México

**Cambios en otras ADRs**
- ADR-0005: `BaseEntity` declara `created_at` y `updated_at` como `timestamptz` mapeados a `DateTimeOffset`
- ADR-0006: logs de Serilog usan `IClock` para timestamps; campos `timestamp` se serializan como ISO 8601 UTC con offset

**Casos a documentar en `CLAUDE.md`**
- Patrón de queries de rango por día/mes (siempre vía `FechaContable`)
- Inyección de `IClock` en handlers (no `DateTime.UtcNow`)
- Convenciones de formato en frontend
- Tratamiento de timestamps externos (SAT, DOF, webhooks)

**ADRs hijo posibles**
- Política específica de cierre de periodo (cuándo, cómo se libera, quién puede reabrir)
- Manejo de feriados nacionales para cálculos de días hábiles
