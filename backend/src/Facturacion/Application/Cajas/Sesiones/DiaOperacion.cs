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
    public static DateOnly HoyLocal(DateTimeOffset ahoraUtc, string? zonaHorariaIana)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(zonaHorariaIana) ? Sucursal.ZonaHorariaDefault : zonaHorariaIana);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahoraUtc, tz).DateTime);
    }
}
