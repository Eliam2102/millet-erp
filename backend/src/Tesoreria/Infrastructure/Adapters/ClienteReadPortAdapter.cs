using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;

namespace Millet.Tesoreria.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="IClienteReadPort"/> (TES-PR7).
///
/// <para>
/// Lectura cross-módulo vía <see cref="CompartidoDbContext"/> sobre
/// <c>compartido.clientes</c> con <c>AsNoTracking</c>, mismo patrón que
/// <see cref="ProveedorBancoReadPortAdapter"/>. Sin filtro de estatus: la
/// bandeja debe seguir mostrando el nombre de un cliente ya inactivo.
/// </para>
/// </summary>
public sealed class ClienteReadPortAdapter : IClienteReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ClienteReadPortAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, ClienteRefDto>> ObtenerVariosAsync(
        IReadOnlyCollection<Guid> clienteIds, CancellationToken cancellationToken)
    {
        if (clienteIds.Count == 0)
            return new Dictionary<Guid, ClienteRefDto>();

        using var bypass = _empresaContext.Bypass();
        return await _db.Clientes.AsNoTracking()
            .Where(c => clienteIds.Contains(c.Id))
            .Select(c => new ClienteRefDto(c.Id, c.Clave, c.RazonSocial))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
    }
}
