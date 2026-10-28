namespace Millet.ProductoTerminado.Application;

/// <summary>Convierte la hora cruda de A+W (Kind Unspecified, hora de Ciudad de México) a UTC.</summary>
public static class HoraAw
{
    private static readonly TimeZoneInfo Zona = Resolver();

    public static DateTimeOffset AUtc(DateTime horaAw)
    {
        var local = DateTime.SpecifyKind(horaAw, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zona.GetUtcOffset(local));
    }

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
