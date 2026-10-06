using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Catalogo;

/// <summary>
/// G1.6: resuelve (AlmacenId, SucursalId) de un sub-almacén para los campos
/// contables opcionales de los eventos de integración. Una sola consulta.
/// </summary>
public static class AlmacenSucursalResolver
{
    public static async Task<(Guid? AlmacenId, Guid? SucursalId)> ResolverAsync(
        AlmacenDbContext db, Guid subAlmacenId, CancellationToken cancellationToken)
    {
        var r = await db.SubAlmacenes.AsNoTracking()
            .Where(s => s.Id == subAlmacenId)
            .Join(db.Almacenes, s => s.AlmacenId, a => a.Id,
                (s, a) => new { AlmacenId = (Guid?)a.Id, SucursalId = (Guid?)a.SucursalId })
            .FirstOrDefaultAsync(cancellationToken);
        return (r?.AlmacenId, r?.SucursalId);
    }
}
