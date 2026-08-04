using MediatR;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Catalogos;

/// <summary>
/// Lookup de productos A+W para el selector de conceptos en emisión
/// (FAC-UX-PR1, cierra PLATFORM-TODO(&lt;ProductoSelector&gt;)). Vive en
/// Facturación —no en Datos Maestros— para que el facturista no requiera
/// permisos de administración de catálogos. Filtros excluyentes al estilo
/// ADR-0045 (referencia externa tiene precedencia sobre descripción).
/// </summary>
public sealed record ProductosAwLookupQuery(
    string? Referencia = null,
    string? Descripcion = null,
    int Limit = 20) : IRequest<IReadOnlyList<ProductoAwLookupItem>>;

public sealed record ProductoAwLookupItem(
    Guid Id,
    string ReferenciaExterna,
    string Descripcion,
    string UnidadMedida,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr,
    string? FraccionArancelaria,
    string? UnidadAduana,
    decimal? PesoUnitarioKg,
    bool DatosFiscalesCompletos);

public sealed class ProductosAwLookupHandler
    : IRequestHandler<ProductosAwLookupQuery, IReadOnlyList<ProductoAwLookupItem>>
{
    private const int LimitMax = 50;
    private readonly IProductosReadPort _productos;

    public ProductosAwLookupHandler(IProductosReadPort productos) => _productos = productos;

    public async Task<IReadOnlyList<ProductoAwLookupItem>> Handle(
        ProductosAwLookupQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit is <= 0 or > LimitMax ? 20 : query.Limit;
        var items = await _productos.BuscarAsync(query.Referencia, query.Descripcion, limit, cancellationToken);

        return items
            .Select(p => new ProductoAwLookupItem(
                p.ProductoId, p.ReferenciaExterna, p.Descripcion, p.UnidadMedida,
                p.ClaveProdServSat, p.ClaveUnidadSat, p.ObjetoImp,
                p.TasaIvaTraslado, p.TasaRetencionIva, p.TasaRetencionIsr,
                p.FraccionArancelaria, p.UnidadAduana, p.PesoUnitarioKg,
                p.ClaveProdServSat != null && p.ClaveUnidadSat != null && p.ObjetoImp != null))
            .ToList();
    }
}
