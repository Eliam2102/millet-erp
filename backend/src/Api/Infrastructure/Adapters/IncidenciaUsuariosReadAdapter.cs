using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Incidencias;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>Nombres de usuario para incidencias (U-4) sobre <c>identidad.usuarios</c>; mismo patrón que el de CxP.</summary>
public sealed class IncidenciaUsuariosReadAdapter(IdentidadDbContext db, ICurrentEmpresaContext empresaContext) : IIncidenciaUsuariosReadPort
{
    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken)
    {
        if (usuarioIds.Count == 0) return new Dictionary<Guid, string>();
        var distinct = usuarioIds.Distinct().ToArray();
        using var bypass = empresaContext.Bypass();
        return await db.Usuarios.AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nombre, cancellationToken);
    }
}
