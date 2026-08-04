using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;

namespace Millet.Tesoreria.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="IProveedorBancoReadPort"/> (TES-PR3).
///
/// <para>
/// Lectura cross-módulo vía <see cref="CompartidoDbContext"/> sobre
/// <c>compartido.proveedores</c> con <c>AsNoTracking</c>, mismo patrón que
/// <c>ClienteReadPortAdapter</c> de CxC. Usa
/// <c>ICurrentEmpresaContext.Bypass()</c> porque también lo invoca el
/// listener de eventos, que corre fuera de un request HTTP.
/// </para>
/// </summary>
public sealed class ProveedorBancoReadPortAdapter : IProveedorBancoReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ProveedorBancoReadPortAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<ProveedorBancoDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();
        return await _db.Proveedores.AsNoTracking()
            .Where(p => p.Id == proveedorId)
            .Select(p => new ProveedorBancoDto(p.Id, p.Clave, p.RazonSocial, p.Banco, p.Clabe, p.Beneficiario))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(
        IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken)
    {
        if (proveedorIds.Count == 0)
            return new Dictionary<Guid, ProveedorBancoDto>();

        using var bypass = _empresaContext.Bypass();
        return await _db.Proveedores.AsNoTracking()
            .Where(p => proveedorIds.Contains(p.Id))
            .Select(p => new ProveedorBancoDto(p.Id, p.Clave, p.RazonSocial, p.Banco, p.Clabe, p.Beneficiario))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
    }
}
