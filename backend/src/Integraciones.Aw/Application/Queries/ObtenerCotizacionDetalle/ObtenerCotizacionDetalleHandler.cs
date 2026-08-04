using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionDetalle;

public sealed class ObtenerCotizacionDetalleHandler
    : IRequestHandler<ObtenerCotizacionDetalleQuery, CotizacionDetalleResponse?>
{
    private const int EnviosRecientesLimit = 10;

    private readonly IntegracionesAwDbContext _db;

    public ObtenerCotizacionDetalleHandler(IntegracionesAwDbContext db)
    {
        _db = db;
    }

    public async Task<CotizacionDetalleResponse?> Handle(
        ObtenerCotizacionDetalleQuery request,
        CancellationToken cancellationToken)
    {
        var entidad = await _db.EntidadesExternas
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.CotizacionId, cancellationToken);

        if (entidad is null) return null;

        var correlacion = await _db.Correlaciones
            .AsNoTracking()
            .Where(c => c.EntidadExternaId == entidad.Id)
            .OrderByDescending(c => c.CorrelatedAt)
            .Select(c => new CorrelacionDetalle(
                c.Id, c.AwDocId, c.AwDocIdSecondary,
                c.PollingCycleNumber, c.PollingQueryDurationMs,
                c.CorrelatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var envios = await _db.Envios
            .AsNoTracking()
            .Where(en => en.EntidadExternaId == entidad.Id)
            .OrderByDescending(en => en.StartedAt)
            .Take(EnviosRecientesLimit)
            .Select(en => new EnvioDetalleItem(
                en.Id, en.AttemptNumber, en.StartedAt, en.FinishedAt,
                en.Status, en.DropServiceUrl, en.BytesSent, en.HttpStatusCode,
                en.ErrorMessage, en.ErrorKind, en.DurationMs))
            .ToListAsync(cancellationToken);

        return new CotizacionDetalleResponse(
            Id: entidad.Id,
            TipoEntidad: entidad.TipoEntidad,
            ReferenciaExterna: entidad.ReferenciaExterna,
            Sucursal: entidad.Sucursal,
            EmpresaId: entidad.EmpresaId,
            Estado: entidad.Estado,
            SubmittedAt: entidad.SubmittedAt,
            DeliveredToAwAt: entidad.DeliveredToAwAt,
            CorrelatedAt: entidad.CorrelatedAt,
            AwDocId: entidad.AwDocId,
            AwDocIdSecondary: entidad.AwDocIdSecondary,
            SubmittedBySpnId: entidad.SubmittedBySpnId,
            LastError: entidad.LastError,
            RetryCount: entidad.RetryCount,
            ResolutionNote: entidad.ResolutionNote,
            // pdf_url apunta al endpoint proxy autenticado, no al blob interno.
            PdfUrl: entidad.PdfBlobUrl is null
                ? null
                : $"/api/v1/integraciones/aw/cotizaciones/{entidad.Id}/pdf",
            PdfFilename: entidad.PdfFilename,
            PdfUploadedAt: entidad.PdfUploadedAt,
            Correlacion: correlacion,
            EnviosRecientes: envios);
    }
}
