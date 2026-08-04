using MediatR;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Catalogos;

/// <summary>
/// Lookup de canales de venta activos para los selectores de captura de
/// pedidos y emisión de CFDI (FAC-ING-PR2). Vive en Facturación —no en
/// Administración— para que el facturista no requiera permisos de
/// administración de catálogos, mismo criterio que
/// <see cref="ClientesLookupQuery"/>. Ordenado por id (orden histórico del
/// enum original).
/// </summary>
public sealed record CanalesVentaLookupQuery() : IRequest<IReadOnlyList<CanalVentaLookupItem>>;

public sealed record CanalVentaLookupItem(short Id, string Nombre);

public sealed class CanalesVentaLookupHandler
    : IRequestHandler<CanalesVentaLookupQuery, IReadOnlyList<CanalVentaLookupItem>>
{
    private readonly ICanalesVentaReadPort _canalesVenta;

    public CanalesVentaLookupHandler(ICanalesVentaReadPort canalesVenta)
        => _canalesVenta = canalesVenta;

    public async Task<IReadOnlyList<CanalVentaLookupItem>> Handle(
        CanalesVentaLookupQuery query, CancellationToken cancellationToken)
    {
        var canales = await _canalesVenta.ListarActivosAsync(cancellationToken);
        return canales.Select(c => new CanalVentaLookupItem(c.Id, c.Nombre)).ToList();
    }
}
