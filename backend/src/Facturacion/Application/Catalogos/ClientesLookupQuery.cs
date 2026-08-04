using MediatR;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Catalogos;

/// <summary>
/// Lookup de clientes para el selector del formulario de emisión
/// (FAC-UX-PR1, cierra PLATFORM-TODO(&lt;ClienteSelector&gt;)). Vive en
/// Facturación —no en Datos Maestros— para que el facturista no requiera
/// permisos de administración de catálogos; expone solo lo necesario para
/// seleccionar receptor y autollenar sus datos fiscales. Filtros excluyentes
/// al estilo ADR-0045 (RFC tiene precedencia sobre razón social).
/// </summary>
public sealed record ClientesLookupQuery(
    string? Rfc = null,
    string? RazonSocial = null,
    int Limit = 20) : IRequest<IReadOnlyList<ClienteLookupItem>>;

public sealed record ClienteLookupItem(
    Guid Id,
    string Clave,
    string RazonSocial,
    string? Rfc,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdiDefault,
    string? FormaPagoDefault,
    string? MetodoPagoDefault,
    string MonedaDefault,
    bool EsGenerico,
    string? NumRegIdTrib,
    string? PaisResidencia,
    string? DomicilioExtranjeroCalle,
    string? DomicilioExtranjeroEstado,
    string? DomicilioExtranjeroCodigoPostal,
    bool DatosFiscalesCompletos);

public sealed class ClientesLookupHandler
    : IRequestHandler<ClientesLookupQuery, IReadOnlyList<ClienteLookupItem>>
{
    private const int LimitMax = 50;
    private readonly IClientesReadPort _clientes;

    public ClientesLookupHandler(IClientesReadPort clientes) => _clientes = clientes;

    public async Task<IReadOnlyList<ClienteLookupItem>> Handle(
        ClientesLookupQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit is <= 0 or > LimitMax ? 20 : query.Limit;
        var items = await _clientes.BuscarAsync(query.Rfc, query.RazonSocial, limit, cancellationToken);

        return items
            .Select(c => new ClienteLookupItem(
                c.ClienteId, c.Clave, c.RazonSocial, c.Rfc, c.RegimenFiscal,
                c.CodigoPostalFiscal, c.UsoCfdiDefault, c.FormaPagoDefault,
                c.MetodoPagoDefault, c.MonedaDefault, c.EsGenerico,
                c.NumRegIdTrib, c.PaisResidencia, c.DomicilioExtranjeroCalle,
                c.DomicilioExtranjeroEstado, c.DomicilioExtranjeroCodigoPostal,
                c.Rfc != null && c.RegimenFiscal != null && c.CodigoPostalFiscal != null))
            .ToList();
    }
}
