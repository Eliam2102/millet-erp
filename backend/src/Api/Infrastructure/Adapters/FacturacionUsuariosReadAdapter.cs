using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ports;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// Adapter de <see cref="IUsuariosReadPort"/> (Facturación) sobre
/// <c>identidad.usuarios</c>. Vive en Api porque Facturación no referencia
/// Identidad. Mismo patrón que el <c>UsuarioReadAdapter</c> de Compras:
/// <c>AsNoTracking</c> + bypass del filtro de empresa (el padrón de usuarios
/// es cross-empresa).
/// </summary>
public sealed class FacturacionUsuariosReadAdapter(
    IdentidadDbContext db,
    ICurrentEmpresaContext empresaContext) : IUsuariosReadPort
{
    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken)
    {
        if (usuarioIds.Count == 0)
            return new Dictionary<Guid, string>();

        var distinct = usuarioIds.Distinct().ToArray();

        using var bypass = empresaContext.Bypass();

        return await db.Usuarios
            .AsNoTracking()
            .Where(u => distinct.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nombre, cancellationToken);
    }
}
