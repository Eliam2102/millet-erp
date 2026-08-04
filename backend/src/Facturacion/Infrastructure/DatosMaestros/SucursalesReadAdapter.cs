using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.DatosMaestros;

/// <summary>
/// Adapter REAL de <see cref="ISucursalesReadPort"/> sobre
/// <c>compartido.sucursales</c> (dueño: Administración). Lectura vía
/// <see cref="CompartidoDbContext"/>, mismo precedente que
/// <see cref="CanalesVentaReadAdapter"/>; cero escritura. El catálogo de
/// sucursales es cross-empresa (sin EmpresaId, MVP mono-empresa), así que no
/// requiere bypass del filtro global.
/// </summary>
public sealed class SucursalesReadAdapter : ISucursalesReadPort
{
    private readonly CompartidoDbContext _db;

    public SucursalesReadAdapter(CompartidoDbContext db) => _db = db;

    public async Task<IReadOnlyList<Guid>> FiltrarNoActivasAsync(
        IReadOnlyCollection<Guid> sucursalIds, CancellationToken cancellationToken)
    {
        if (sucursalIds.Count == 0) return [];

        var distinct = sucursalIds.Distinct().ToArray();
        var activas = await _db.Sucursales.AsNoTracking()
            .Where(s => distinct.Contains(s.Id) && s.Estatus == EstatusCatalogo.Activo)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        return distinct.Except(activas).ToList();
    }

    public async Task<string?> ObtenerZonaHorariaAsync(Guid sucursalId, CancellationToken cancellationToken)
    {
        return await _db.Sucursales.AsNoTracking()
            .Where(s => s.Id == sucursalId)
            .Select(s => s.ZonaHoraria)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
