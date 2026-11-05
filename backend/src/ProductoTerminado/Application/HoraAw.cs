namespace Millet.ProductoTerminado.Application;

/// <summary>Convierte la hora cruda de A+W (Kind Unspecified, hora de Ciudad de México) a UTC.</summary>
public static class HoraAw
{
    private static readonly TimeZoneInfo Zona = Resolver();

    public static DateTimeOffset AUtc(DateTime horaAw)
    {
        var local = DateTime.SpecifyKind(horaAw, DateTimeKind.Unspecified);
        // Npgsql solo acepta offset 0 en timestamptz.
        return new DateTimeOffset(local, Zona.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Día calendario en Ciudad de México de un instante (para «el mismo día»).</summary>
    public static DateOnly FechaLocal(DateTimeOffset instante) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, Zona).DateTime);

    public static int HoraLocal(DateTimeOffset instante) => TimeZoneInfo.ConvertTime(instante, Zona).Hour;

    private static TimeZoneInfo Resolver()
    {
        foreach (var id in new[] { "America/Mexico_City", "Central Standard Time (Mexico)" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new TimeZoneNotFoundException("No se encontró la zona horaria de Ciudad de México.");
    }
}
