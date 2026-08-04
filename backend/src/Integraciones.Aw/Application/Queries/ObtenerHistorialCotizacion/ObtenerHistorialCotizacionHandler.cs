using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Application.Queries.ObtenerHistorialCotizacion;

public sealed class ObtenerHistorialCotizacionHandler
    : IRequestHandler<ObtenerHistorialCotizacionQuery, HistorialResponse?>
{
    private readonly IntegracionesAwDbContext _db;

    public ObtenerHistorialCotizacionHandler(IntegracionesAwDbContext db)
    {
        _db = db;
    }

    public async Task<HistorialResponse?> Handle(
        ObtenerHistorialCotizacionQuery request,
        CancellationToken cancellationToken)
    {
        var entidad = await _db.EntidadesExternas
            .AsNoTracking()
            .Where(e => e.Id == request.CotizacionId)
            .Select(e => new { e.Id, e.ReferenciaExterna, e.Sucursal })
            .FirstOrDefaultAsync(cancellationToken);

        if (entidad is null) return null;

        var envios = await _db.Envios
            .AsNoTracking()
            .Where(en => en.EntidadExternaId == entidad.Id)
            .OrderBy(en => en.AttemptNumber) // cronológico
            .Select(en => new HistorialItem(
                en.Id, en.AttemptNumber, en.StartedAt, en.FinishedAt,
                en.Status, en.DropServiceUrl, en.BytesSent, en.HttpStatusCode,
                en.ErrorMessage, en.ErrorKind, en.DurationMs, en.Filename))
            .ToListAsync(cancellationToken);

        return new HistorialResponse(
            CotizacionId: entidad.Id,
            ReferenciaExterna: entidad.ReferenciaExterna,
            Sucursal: entidad.Sucursal,
            Envios: envios);
    }
}
