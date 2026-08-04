using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IProveedorReadPort"/>
/// declarado en <c>Almacen.Domain.Ports</c>. Reemplaza el
/// <c>NoOpProveedorReadPort</c> de Almacén
/// (PLATFORM-TODO &lt;ProveedorReadAdapter&gt;).
///
/// <para>Lectura cross-módulo via <see cref="CompartidoDbContext"/> con
/// <c>AsNoTracking</c>. Usa <c>ICurrentEmpresaContext.Bypass()</c>
/// — proveedores son catálogo cross-empresa.</para>
///
/// <para>Lo invoca el sub-flujo 8.B (devolución a proveedor) cuando el
/// handler necesita validar que el proveedor existe y está activo antes
/// de generar el movimiento + evento <c>OcDevolucionRegistradaEvent</c>.</para>
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

    public async Task<ProveedorLectura?> ObtenerAsync(
        Guid proveedorId,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        var proveedor = await _db.Proveedores
            .AsNoTracking()
            .Where(p => p.Id == proveedorId)
            .Select(p => new ProveedorLectura(
                p.Id,
                p.Clave,
                p.RazonSocial,
                p.Rfc,
                p.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);

        return proveedor;
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
            .Select(p => new ProveedorLectura(
                p.Id,
                p.Clave,
                p.RazonSocial,
                p.Rfc,
                p.Estatus == EstatusCatalogo.Activo))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }
}
