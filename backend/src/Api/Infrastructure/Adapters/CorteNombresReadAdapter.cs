using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// Adapter de <see cref="ICorteNombresReadPort"/>: cajeros desde <c>identidad.usuarios</c> (bypass del filtro de
/// empresa, padrón cross-empresa) y cajas desde Facturación. Vive en Api porque Tesorería no referencia Facturación.
/// </summary>
public sealed class CorteNombresReadAdapter(
    IdentidadDbContext identidad, FacturacionDbContext facturacion, ICurrentEmpresaContext empresaContext) : ICorteNombresReadPort
{
    public async Task<IReadOnlyDictionary<Guid, string>> NombresCajerosAsync(
        IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken)
    {
        var ids = usuarioIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        using var bypass = empresaContext.Bypass();
        return await identidad.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nombre, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NombresCajasAsync(
        IReadOnlyCollection<Guid> cajaIds, CancellationToken cancellationToken)
    {
        var ids = cajaIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        return await facturacion.Cajas.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nombre, cancellationToken);
    }
}
