namespace Millet.SharedKernel.Application;

/// <summary>
/// Helpers de fecha conscientes de la zona horaria de operación del ERP
/// (<c>America/Mexico_City</c>). Implementa la pieza que ADR-0013 especificó
/// pero no se había construido, y es el <b>punto único de verdad</b> para
/// derivar el día calendario operativo a partir de un instante UTC
/// (ADR-0040, regla 4: el "hoy" de una fecha de negocio se calcula en hora
/// local de México, nunca <c>DateOnly.FromDateTime(UtcNow)</c> que daría la
/// fecha UTC y adelantaría el día después de las 18:00 hora local).
///
/// <para>México eliminó el horario de verano en 2022; <c>tzdata</c> lo
/// refleja (ADR-0013). El id IANA <c>America/Mexico_City</c> lo resuelve
/// .NET 6+ cross-platform vía ICU.</para>
/// </summary>
public static class FechaContable
{
    /// <summary>
    /// Zona horaria de operación del ERP. Constante hardcodeada por ADR-0040
    /// (decisión B); el parámetro global <c>system.timezone-default</c> queda
    /// como documentación.
    /// </summary>
    public static readonly TimeZoneInfo ZonaOperacion =
        TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    /// <summary>
    /// Día calendario, en hora local de México, correspondiente al instante
    /// UTC dado.
    /// </summary>
    public static DateOnly HoyLocal(DateTimeOffset instanteUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instanteUtc, ZonaOperacion).DateTime);

    /// <summary>
    /// Instante UTC correspondiente a la medianoche (inicio del día) en hora
    /// local de México de la fecha de calendario dada — la pieza de "bounds"
    /// que ADR-0013 esbozó. Útil cuando una <see cref="DateOnly"/> de negocio
    /// tiene que materializarse como timestamp (ej. un nodo del árbol de
    /// trazabilidad cuyo campo es un instante) sin introducir off-by-one:
    /// <c>2026-06-07</c> → <c>2026-06-07T06:00:00Z</c>.
    /// </summary>
    public static DateTimeOffset InicioDelDiaUtc(DateOnly fecha)
    {
        var medianocheLocal = fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(medianocheLocal, ZonaOperacion.GetUtcOffset(medianocheLocal))
            .ToUniversalTime();
    }

    /// <summary>
    /// Día calendario de hoy en hora local de México, derivado del reloj
    /// inyectado. Determinista en tests (controlable vía <c>TestClock</c> /
    /// <c>FakeClock</c>). No usa <c>DateTime.UtcNow</c> — cumple
    /// <c>BannedApiAnalyzers</c> (ADR-0013).
    /// </summary>
    public static DateOnly HoyLocal(this IClock clock) => HoyLocal(clock.UtcNow);
}
