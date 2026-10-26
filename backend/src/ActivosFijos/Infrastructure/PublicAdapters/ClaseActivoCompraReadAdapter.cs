using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports;
namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;
public sealed class ClaseActivoCompraReadAdapter(ActivosFijosDbContext db) : IClaseActivoCompraReadPort
{
    public Task<bool> EsActivaAsync(Guid id, CancellationToken ct) => db.Clases.AnyAsync(x => x.Id == id && x.Activa, ct);
}
