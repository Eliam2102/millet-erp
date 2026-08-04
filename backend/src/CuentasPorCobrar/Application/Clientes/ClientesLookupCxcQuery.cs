using MediatR;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;

namespace Millet.CuentasPorCobrar.Application.Clientes;

// ============================================================================
// CXC-FE-PR2: lookup de clientes para las bandejas y selectores del frontend
// CxC. El 01-diseño §7 ya asigna a IClienteReadPort el rol "nombre/RFC del
// cliente para bandejas y reportes"; este query lo expone vía HTTP con
// permisos del módulo (mismo criterio que FacturacionCatalogosEndpoints: el
// usuario de CxC no requiere permisos de administración de Datos Maestros).
// ============================================================================

public sealed record ClientesLookupCxcQuery(
    string? Rfc,
    string? RazonSocial,
    IReadOnlyCollection<Guid>? Ids,
    int Limit = 20) : IRequest<IReadOnlyList<ClienteLookupCxcDto>>;

public sealed class ClientesLookupCxcHandler
    : IRequestHandler<ClientesLookupCxcQuery, IReadOnlyList<ClienteLookupCxcDto>>
{
    private const int LimitMax = 500;
    private readonly IClienteReadPort _clientes;

    public ClientesLookupCxcHandler(IClienteReadPort clientes) => _clientes = clientes;

    public Task<IReadOnlyList<ClienteLookupCxcDto>> Handle(
        ClientesLookupCxcQuery query, CancellationToken cancellationToken)
    {
        // El modo ids resuelve una página completa de bandeja (hasta 500
        // renglones, tope del ListarLineasCreditoHandler); los modos de
        // búsqueda usan el default del combobox.
        var limit = query.Limit is <= 0 or > LimitMax ? 20 : query.Limit;
        return _clientes.BuscarAsync(
            query.Rfc, query.RazonSocial, query.Ids, limit, cancellationToken);
    }
}
