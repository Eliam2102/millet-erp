using MediatR;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionPdf;

/// <summary>
/// Resuelve la referencia interna del PDF de A+W (oferta/pedido) adjunto a
/// una cotización, para que el endpoint proxy
/// <c>GET /cotizaciones/{id}/pdf</c> sirva el stream. El global query
/// filter de empresa (ADR-0011) garantiza que solo se resuelve el PDF de
/// la empresa del JWT.
/// </summary>
public sealed record ObtenerCotizacionPdfQuery(Guid CotizacionId)
    : IRequest<CotizacionPdfRef?>;

/// <summary>
/// Referencia interna del PDF: URL de blob (NO se expone al cliente) +
/// nombre de archivo original para el <c>fileDownloadName</c>.
/// </summary>
public sealed record CotizacionPdfRef(string BlobUrl, string Filename);
