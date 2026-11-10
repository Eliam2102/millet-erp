using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application.Calendario;

namespace Millet.ProductoTerminado.Infrastructure.PublicAdapters;

/// <summary>Lee sucursales y festivos de <c>compartido</c> sin escribir (mismo precedente que <c>SucursalesReadAdapter</c> de Facturación).</summary>
public sealed class PtCalendarioSucursalesAdapter(CompartidoDbContext db) : IPtCalendarioSucursalesPort
{
    public async Task<IReadOnlyDictionary<Guid, SucursalCalendarioPt>> SucursalesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Sucursales.AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new SucursalCalendarioPt(s.Clave, s.ZonaHoraria), ct);

    public async Task<IReadOnlySet<DateOnly>> FestivosAsync(CancellationToken ct)
    {
        var valor = await db.ParametrosGlobales.AsNoTracking().Where(p => p.Clave == CalendarioHabil.ClaveFestivos).Select(p => p.Valor).FirstOrDefaultAsync(ct);
        return valor is null ? new HashSet<DateOnly>() : CalendarioHabil.LeerFestivos(valor);
    }
}
