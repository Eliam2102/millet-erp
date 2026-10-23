using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
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
        var varios = await ObtenerVariosAsync([proveedorId], cancellationToken);
        return varios.GetValueOrDefault(proveedorId);
    }

    public async Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(
        IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken)
    {
        if (proveedorIds.Count == 0)
            return new Dictionary<Guid, ProveedorBancoDto>();

        using var bypass = _empresaContext.Bypass();
        var proveedores = await _db.Proveedores.AsNoTracking()
            .Where(p => proveedorIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Clave, p.RazonSocial, p.Estatus, p.Rfc })
            .ToListAsync(cancellationToken);
        var cuentas = (await _db.CuentasBancariasProveedor.AsNoTracking()
                .Where(c => proveedorIds.Contains(c.ProveedorId) && c.Estado != EstadoCuentaBancariaProveedor.Rechazada)
                .ToListAsync(cancellationToken))
            .ToLookup(c => c.ProveedorId, c => new CuentaProveedorDto(
                c.Id, c.Banco, c.Identificador, c.Clabe is not null, c.Swift, c.Moneda, c.Titular, c.Predeterminada,
                c.Estado.ToString()));
        return proveedores.ToDictionary(p => p.Id, p => new ProveedorBancoDto(
            p.Id, p.Clave, p.RazonSocial, cuentas[p.Id].ToList(),
            p.Estatus == EstatusCatalogo.Activo, p.Estatus == EstatusCatalogo.EnRevision, p.Rfc));
    }
}
