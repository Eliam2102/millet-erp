using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="IProveedorReadPort"/>. Reemplaza
/// <see cref="Stubs.NoOpProveedorReadPort"/> (PLATFORM-TODO &lt;ProveedorReadPort&gt;).
///
/// <para>Lectura cross-módulo via <see cref="CompartidoDbContext"/> sobre
/// <c>compartido.proveedores</c> con <c>AsNoTracking</c>. Usa
/// <c>ICurrentEmpresaContext.Bypass()</c> porque el catálogo de
/// proveedores es cross-empresa.</para>
///
/// <para>
/// Lo invoca CxP al capturar facturas, NCs y anticipos para resolver
/// RFC, razón social y flags operativos (§6.1 del 01-diseno).
/// </para>
///
/// <para><b>Mapeo a <see cref="ProveedorDto"/></b>:</para>
/// <list>
///   <item><c>EnRevision</c> = <see cref="Millet.DatosMaestros.Domain.Proveedor.Estatus"/> == <see cref="EstatusCatalogo.EnRevision"/>.</item>
///   <item><c>Activo</c> = <c>Estatus</c> == <see cref="EstatusCatalogo.Activo"/>.</item>
///   <item><c>Tolerancia</c> = <c>null</c>: <see cref="Millet.DatosMaestros.Domain.Proveedor"/>
///   aún no modela tolerancia por proveedor (PLATFORM-TODO
///   &lt;CxpTolerancias&gt; — feature deferred). Los handlers degradan a la
///   tolerancia default global del módulo. Cuando entre la migración
///   aditiva, este mapping se actualiza sin contrato break.</item>
/// </list>
/// </summary>
public sealed class ProveedorReadPortAdapter : IProveedorReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ProveedorReadPortAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<ProveedorDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();

        return await _db.Proveedores
            .AsNoTracking()
            .Where(p => p.Id == proveedorId)
            .Select(p => new ProveedorDto(
                p.Id,
                p.Rfc,
                p.RazonSocial,
                null,
                p.Estatus == EstatusCatalogo.EnRevision,
                p.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rfc)) return null;

        using var bypass = _empresaContext.Bypass();

        var rfcNormalizado = rfc.Trim().ToUpperInvariant();

        return await _db.Proveedores
            .AsNoTracking()
            .Where(p => p.Rfc == rfcNormalizado)
            .OrderBy(p => p.Estatus == EstatusCatalogo.Activo ? 0 : 1)
            .ThenBy(p => p.Clave)
            .Select(p => new ProveedorDto(
                p.Id,
                p.Rfc,
                p.RazonSocial,
                null,
                p.Estatus == EstatusCatalogo.EnRevision,
                p.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken)
    {
        if (proveedorIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        using var bypass = _empresaContext.Bypass();

        return await _db.Proveedores
            .AsNoTracking()
            .Where(p => proveedorIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.RazonSocial, cancellationToken);
    }
}
