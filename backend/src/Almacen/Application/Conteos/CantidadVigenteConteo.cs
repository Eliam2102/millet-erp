using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Conteos;

/// <summary>
/// Cantidad vigente de las líneas de un conteo (A7). Regla única usada por la
/// comparación, la evaluación de variaciones, la aprobación y la aplicación:
/// el último recuento (mayor <c>Secuencia</c>) reemplaza a la captura original
/// como cantidad real; la captura original se conserva en <c>CantidadRealCapturada</c>.
/// </summary>
internal static class CantidadVigenteConteo
{
    /// <summary>Mapa <c>lineaId → cantidad del último recuento</c>. Solo incluye líneas con recuento.</summary>
    public static async Task<IReadOnlyDictionary<Guid, decimal>> UltimosRecuentosAsync(
        AlmacenDbContext db, Guid conteoId, CancellationToken cancellationToken)
    {
        var recuentos = await db.Set<RecuentoConteo>().AsNoTracking()
            .Where(r => db.Set<LineaConteo>().Any(l => l.Id == r.LineaConteoId && l.ConteoId == conteoId))
            .ToListAsync(cancellationToken);

        return recuentos
            .GroupBy(r => r.LineaConteoId)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Secuencia)!.CantidadRecontada);
    }
}
