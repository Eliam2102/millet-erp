using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Infrastructure.Calendario;

public sealed class CalendarioHabilService(CompartidoDbContext db) : ICalendarioHabil
{
    public async Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct)
    {
        var parametros = await db.ParametrosGlobales.AsNoTracking()
            .Where(p => p.Clave == CalendarioHabil.ClaveFestivos || p.Clave == "system.timezone-default")
            .ToDictionaryAsync(p => p.Clave, p => p.Valor, ct);
        if (!parametros.TryGetValue(CalendarioHabil.ClaveFestivos, out var valor))
            throw new BusinessRuleException("CALENDARIO_NO_CONFIGURADO", "Configura los días festivos en Administración antes de registrar el vale.");
        var zona = TimeZoneInfo.FindSystemTimeZoneById(parametros.GetValueOrDefault("system.timezone-default", "America/Mexico_City"));
        return CalendarioHabil.SumarHoras(fecha, horas, CalendarioHabil.LeerFestivos(valor), zona);
    }
}
