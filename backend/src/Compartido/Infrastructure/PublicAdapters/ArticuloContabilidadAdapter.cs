using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Ports;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>Adaptador de <see cref="IArticuloContabilidadPort"/> (F1-ALM-13): grupo (categoría) de cada artículo, aunque esté dado de baja.</summary>
public sealed class ArticuloContabilidadAdapter(CompartidoDbContext db) : IArticuloContabilidadPort
{
    public async Task<IReadOnlyDictionary<Guid, string>> GruposAsync(IReadOnlyCollection<Guid> articuloIds, CancellationToken ct)
    {
        var ids = articuloIds.Distinct().ToArray();
        return await db.Articulos.IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.Id) && a.Categoria != null)
            .Select(a => new { a.Id, a.Categoria }).ToDictionaryAsync(a => a.Id, a => a.Categoria!, ct);
    }
}
