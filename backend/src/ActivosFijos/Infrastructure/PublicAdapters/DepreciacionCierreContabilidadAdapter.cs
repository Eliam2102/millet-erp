using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Cierre;

namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;

/// <summary>
/// CON-12 (86e3jvvme): ¿la depreciación del mes está aplicada (corrida no anulada)? Si no, si hay activos que ya deprecian en ese mes
/// (primer mes de depreciación del libro ≤ el mes). Solo lectura.
/// </summary>
public sealed class DepreciacionCierreContabilidadAdapter(ActivosFijosDbContext db, LibroServicio libro) : IDepreciacionCierreReadPort
{
    public async Task<EstadoDepreciacionMes> EstadoAsync(int anio, int mes, CancellationToken ct)
    {
        var periodo = new DateOnly(anio, mes, 1);
        if (await db.Set<CorridaDepreciacion>().AsNoTracking().AnyAsync(x => !x.Anulada && x.Periodo == periodo, ct))
            return new(true, true);
        var primero = await libro.PrimerMesAsync(ct);
        return new(primero is { } p && p <= periodo, false);
    }
}
