using MediatR;

namespace Millet.Compras.Application.Oc.ListarUltimas100Compras;

/// <summary>
/// Historial de las últimas 100 compras de un artículo (F7-PR3, §8.5
/// del 01-diseño). Permite al comprador ver tendencia de precios y
/// proveedores para un material específico.
///
/// <para>
/// Devuelve líneas de OC con cantidad &gt; 0 (la línea fue
/// efectivamente capturada), ordenadas por <c>OC.FechaDocumento DESC</c>.
/// Cap de 100 hardcoded — extender el cap requiere un argumento explícito
/// pero por ahora no se justifica.
/// </para>
/// </summary>
public sealed record ListarUltimas100ComprasQuery(
    Guid ArticuloId,
    Guid? ProveedorId = null,
    DateOnly? FechaDesde = null,
    decimal? CantidadMinima = null) : IRequest<ListarUltimas100ComprasResponse>;

public sealed record ListarUltimas100ComprasResponse(
    IReadOnlyList<CompraMaterialResumen> Items);

public sealed record CompraMaterialResumen(
    Guid LineaId,
    Guid OrdenCompraId,
    string FolioOc,
    Guid ProveedorId,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioUnitario,
    string Moneda,
    DateOnly FechaDocumento);
