using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IProveedorReadPort"/> de Compras
/// (ADR-0042). Resuelve <c>proveedorId → (clave, razón social)</c> leyendo
/// <c>compartido.proveedores</c> via <see cref="CompartidoDbContext"/>.
///
/// <para>
/// Vive en <c>Compras.Infrastructure</c> (el consumidor) y lee del DbContext
/// del data-owner; no hay ciclo (Compartido no referencia Compras), mismo
/// patrón que <see cref="UsuarioReadAdapter"/> y <see cref="ArticuloReadAdapter"/>.
/// </para>
///
/// <para>
/// <c>AsNoTracking</c> + <c>ICurrentEmpresaContext.Bypass()</c>: el catálogo
/// es cross-empresa. No requiere <c>IgnoreQueryFilters()</c>: <c>Proveedor</c>
/// solo implementa <c>IAuditable</c> (no <c>IFiscalmenteRelevante</c>), así que
/// no hay filtro global de soft-delete — un proveedor desactivado/borrado igual
/// resuelve su nombre en req/OC históricas.
/// </para>
/// </summary>
public sealed class ProveedorReadAdapter : IProveedorReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ProveedorReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken)
    {
        if (proveedorIds.Count == 0)
        {
            return new Dictionary<Guid, ProveedorLectura>();
        }

        var distinct = proveedorIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        return await _db.Proveedores
            .AsNoTracking()
            .Where(p => distinct.Contains(p.Id))
            .Select(p => new ProveedorLectura(p.Id, p.Clave, p.RazonSocial))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }
}
