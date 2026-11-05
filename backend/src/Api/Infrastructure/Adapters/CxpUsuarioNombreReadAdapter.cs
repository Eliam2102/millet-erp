using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>Adapter de <see cref="IUsuarioNombreReadPort"/> (CxP) sobre <c>identidad.usuarios</c>; mismo patrón que el de Facturación.</summary>
public sealed class CxpUsuarioNombreReadAdapter(
    IdentidadDbContext db,
    ICurrentEmpresaContext empresaContext) : IUsuarioNombreReadPort
{
    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken)
    {
        if (usuarioIds.Count == 0) return new Dictionary<Guid, string>();
        var distinct = usuarioIds.Distinct().ToArray();
        using var bypass = empresaContext.Bypass();
        return await db.Usuarios.AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nombre, cancellationToken);
    }
}
