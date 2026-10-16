using Millet.Administracion.Domain;

namespace Millet.Facturacion.Application.Cajas.Sesiones;

/// <summary>
/// Día de operación de una sesión de caja (`[Decisión 12-8]`): fecha local en
/// la zona horaria IANA de la sucursal de operación. Los timestamps persisten
/// en UTC; solo el corte y el bloqueo de día anterior (§5.2) razonan en local.
/// </summary>
public static class DiaOperacion
{
    /// <summary>Fecha local "hoy" en la zona dada; cae al default de sucursal (Yucatán) si la zona viene nula.</summary>
    public static DateOnly HoyLocal(DateTimeOffset ahoraUtc, string? zonaHorariaIana) =>
        DateOnly.FromDateTime(ALocal(ahoraUtc, zonaHorariaIana).DateTime);

    /// <summary>Instante en la hora local de la sucursal (misma regla de zona que <see cref="HoyLocal"/>).</summary>
    public static DateTimeOffset ALocal(DateTimeOffset instante, string? zonaHorariaIana)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(zonaHorariaIana) ? Sucursal.ZonaHorariaDefault : zonaHorariaIana);
        return TimeZoneInfo.ConvertTime(instante, tz);
    }

    /// <summary>
    /// Instante (en UTC, como lo exige Npgsql) en que termina el día de
    /// operación: la medianoche local siguiente.
    /// </summary>
    public static DateTimeOffset FinDelDia(DateOnly dia, string? zonaHorariaIana)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(zonaHorariaIana) ? Sucursal.ZonaHorariaDefault : zonaHorariaIana);
        var medianoche = dia.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(medianoche, tz.GetUtcOffset(medianoche)).ToUniversalTime();
    }
}
