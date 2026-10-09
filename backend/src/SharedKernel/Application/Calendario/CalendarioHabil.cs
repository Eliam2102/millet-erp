using System.Globalization;
using System.Text.Json;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Application.Calendario;

public static class CalendarioHabil
{
    public const string ClaveFestivos = "system.dias-festivos";
    // Art. 74 LFT: dato a validar por Millet; incluye la jornada federal del 6-jun-2027.
    // Fuente: https://www.profedet.gob.mx/micrositio/index.php/dias-de-descanso
    // Jornada electoral: https://portal.ine.mx/voto-y-elecciones/elecciones-2027/
    public const string FestivosIniciales = "[\"2026-01-01\",\"2026-02-02\",\"2026-03-16\",\"2026-05-01\",\"2026-09-16\",\"2026-11-16\",\"2026-12-25\",\"2027-01-01\",\"2027-02-01\",\"2027-03-15\",\"2027-05-01\",\"2027-06-06\",\"2027-09-16\",\"2027-11-15\",\"2027-12-25\"]";

    public static IReadOnlySet<DateOnly> LeerFestivos(string valor)
    {
        try
        {
            var fechas = JsonSerializer.Deserialize<string[]>(valor) ?? throw new JsonException();
            return fechas.Select(f => DateOnly.ParseExact(f, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToHashSet();
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            throw new BusinessRuleException("CALENDARIO_FESTIVOS_INVALIDOS", "Los días festivos deben ser una lista JSON de fechas válidas con formato AAAA-MM-DD.");
        }
    }

    public static DateTimeOffset SumarHoras(DateOnly fecha, int horas, IReadOnlySet<DateOnly> festivos, TimeZoneInfo zona)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(horas);
        // FechaMovimiento es DateOnly: se cuentan desde medianoche local,
        // con 24 horas por día hábil (D8 no define una jornada laboral).
        var cursor = fecha.ToDateTime(TimeOnly.MinValue);
        var restantes = horas;
        while (restantes > 0)
        {
            if (cursor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || festivos.Contains(DateOnly.FromDateTime(cursor)))
            {
                cursor = cursor.Date.AddDays(1);
                continue;
            }
            var tramo = Math.Min(restantes, 24);
            cursor = cursor.AddHours(tramo);
            restantes -= tramo;
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(cursor, zona));
    }
}
