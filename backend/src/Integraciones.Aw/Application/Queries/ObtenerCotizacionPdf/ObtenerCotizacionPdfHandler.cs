using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionPdf;

public sealed class ObtenerCotizacionPdfHandler
    : IRequestHandler<ObtenerCotizacionPdfQuery, CotizacionPdfRef?>
{
    private readonly IntegracionesAwDbContext _db;

    public ObtenerCotizacionPdfHandler(IntegracionesAwDbContext db)
    {
        _db = db;
    }

    public async Task<CotizacionPdfRef?> Handle(
        ObtenerCotizacionPdfQuery request,
        CancellationToken cancellationToken)
    {
        var row = await _db.EntidadesExternas
            .AsNoTracking()
            .Where(e => e.Id == request.CotizacionId
                     && e.PdfBlobUrl != null
                     && e.PdfFilename != null)
            .Select(e => new CotizacionPdfRef(e.PdfBlobUrl!, e.PdfFilename!))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
    }
}
