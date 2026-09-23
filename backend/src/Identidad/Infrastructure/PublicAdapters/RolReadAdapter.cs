using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;

namespace Millet.Identidad.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IRolReadPort"/> declarado en
/// <c>Compartido.Application.Administracion.Abstractions</c> (F1-ADM-01.4).
/// Resuelve consultas de roles leyendo <c>identidad.roles</c> vía <see cref="IdentidadDbContext"/>.
/// </summary>
public sealed class RolReadAdapter : IRolReadPort
{
    private readonly IdentidadDbContext _db;

    public RolReadAdapter(IdentidadDbContext db) => _db = db;

    public async Task<bool> ExisteActivoAsync(Guid rolId, CancellationToken cancellationToken)
    {
        return await _db.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Id == rolId && r.Activo, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(
        IEnumerable<Guid> rolIds, CancellationToken cancellationToken)
    {
        var distinctIds = rolIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _db.Roles
            .AsNoTracking()
            .Where(r => distinctIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Nombre, cancellationToken);
    }
}
