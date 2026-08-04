using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Adapter de <see cref="ISucursalPorClaveAwResolver"/> sobre el catálogo
/// compartido de sucursales. Solo machea sucursales activas: ingestar hacia
/// una sucursal desactivada debe caer a la bandeja, no pasar en silencio.
/// </summary>
public sealed class SucursalPorClaveAwResolver : ISucursalPorClaveAwResolver
{
    private readonly CompartidoDbContext _db;

    public SucursalPorClaveAwResolver(CompartidoDbContext db) => _db = db;

    public async Task<Guid?> ResolverAsync(
        string claveAw, CancellationToken cancellationToken)
    {
        var clave = claveAw.Trim().ToUpperInvariant();

        // ToUpper() dentro de la query se traduce a upper() en Postgres —
        // no corre en .NET, por eso se silencian los analyzers de cultura.
#pragma warning disable CA1304, CA1311, CA1862
        return await _db.Sucursales.AsNoTracking()
            .Where(s => s.ClaveAw != null
                     && s.ClaveAw.ToUpper() == clave
                     && s.Estatus == EstatusCatalogo.Activo)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
    }
}
