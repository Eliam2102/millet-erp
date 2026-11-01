namespace Millet.CuentasPorPagar.Application.Periodos;

/// <summary>
/// Día de negocio en México. Con UTC, lo que se registra después de las 18:00 del último día
/// del mes caía en el mes siguiente y chocaba con su periodo contable.
/// </summary>
public static class FechaNegocio
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    public static DateOnly Hoy(DateTimeOffset utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, Zona).DateTime);
}
