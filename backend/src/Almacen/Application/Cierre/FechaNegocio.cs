namespace Millet.Almacen.Application.Cierre;

/// <summary>
/// Día local de negocio (America/Mexico_City). Evita que un movimiento capturado
/// tarde en la noche del último día del mes quede fechado en el mes siguiente por UTC.
/// </summary>
internal static class FechaNegocio
{
    private static readonly TimeZoneInfo Zona = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    public static DateOnly Hoy(DateTimeOffset utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, Zona).DateTime);
}
