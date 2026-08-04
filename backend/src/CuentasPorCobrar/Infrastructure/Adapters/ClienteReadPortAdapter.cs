using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="IClienteReadPort"/> (CXC-PR3).
///
/// <para>Lectura cross-módulo via <see cref="CompartidoDbContext"/> sobre
/// <c>compartido.clientes</c> (master ADR-0048 D6) con <c>AsNoTracking</c>,
/// mismo patrón que <c>ProveedorReadPortAdapter</c> de CxP. Usa
/// <c>ICurrentEmpresaContext.Bypass()</c> porque el listener de eventos
/// corre fuera de un request HTTP.</para>
///
/// <para>La resolución por RFC excluye clientes genéricos
/// (<c>EsGenerico</c>): el público general no participa del crédito. Si
/// varios clientes comparten RFC (sucursales del mismo contribuyente),
/// gana el primero por clave — la vinculación fina se corrige después
/// desde la cartera.</para>
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

    public async Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rfc)) return null;
        var normalizado = rfc.Trim().ToUpperInvariant();

        using var bypass = _empresaContext.Bypass();
        var cliente = await _db.Clientes.AsNoTracking()
            .Where(c => c.Rfc == normalizado && !c.EsGenerico)
            .OrderBy(c => c.Clave)
            .FirstOrDefaultAsync(cancellationToken);

        return cliente is null
            ? null
            : new ClienteRefDto(cliente.Id, cliente.Rfc, cliente.RazonSocial, cliente.EsGenerico);
    }

    public async Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();
        var cliente = await _db.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken);

        return cliente is null
            ? null
            : new ClienteRefDto(cliente.Id, cliente.Rfc, cliente.RazonSocial, cliente.EsGenerico);
    }

    public async Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
        string? rfc,
        string? razonSocial,
        IReadOnlyCollection<Guid>? ids,
        int limit,
        CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();
        IQueryable<Cliente> q = _db.Clientes.AsNoTracking()
            .Where(c => !c.EsGenerico);

        if (ids is { Count: > 0 })
        {
            // Resolución de nombres por página de bandeja: ids exactos,
            // sin filtro de estatus (una línea puede apuntar a un cliente
            // ya inactivo y su nombre debe seguir mostrándose).
            q = q.Where(c => ids.Contains(c.Id));
        }
        else
        {
            q = q.Where(c => c.Estatus == EstatusCatalogo.Activo);

            // Filtros excluyentes al estilo ADR-0045 (RFC tiene precedencia).
#pragma warning disable CA1304, CA1311, CA1862
            if (!string.IsNullOrWhiteSpace(rfc))
            {
                q = q.Where(c => c.Rfc != null && c.Rfc.ToLower().Contains(rfc.ToLower()));
            }
            else if (!string.IsNullOrWhiteSpace(razonSocial))
            {
                q = q.Where(c =>
                    PostgresFunctions.Translate(c.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                        .Contains(PostgresFunctions.Translate(razonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
            }
#pragma warning restore CA1304, CA1311, CA1862
        }

        return await q
            .OrderBy(c => c.Clave)
            .Take(limit)
            .Select(c => new ClienteLookupCxcDto(c.Id, c.Clave, c.Rfc, c.RazonSocial))
            .ToListAsync(cancellationToken);
    }
}
