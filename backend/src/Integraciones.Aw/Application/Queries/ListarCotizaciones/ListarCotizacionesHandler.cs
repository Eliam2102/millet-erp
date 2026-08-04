using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Application.Queries.ListarCotizaciones;

public sealed class ListarCotizacionesHandler
    : IRequestHandler<ListarCotizacionesQuery, PagedResponse<CotizacionResumenItem>>
{
    private const int MaxLimit = 200;
    private const int DefaultLimit = 50;
    private const int MinSearchLength = 4;

    private readonly IntegracionesAwDbContext _db;

    public ListarCotizacionesHandler(IntegracionesAwDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<CotizacionResumenItem>> Handle(
        ListarCotizacionesQuery request,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit <= 0 ? DefaultLimit : request.Limit, 1, MaxLimit);
        var offset = Math.Max(0, request.Offset);

        // Query base — el global query filter de empresa se aplica solo.
        // Filtramos solo cotizaciones (en fase 1 es lo único que llega vía
        // este endpoint; los otros tipos los expone GET /pedidos en futuro).
        var query = _db.EntidadesExternas
            .AsNoTracking()
            .Where(e => e.TipoEntidad == TipoEntidad.Cotizacion);

        if (request.Estado is { } estado)
        {
            query = query.Where(e => e.Estado == estado);
        }
        if (request.Desde is { } desde)
        {
            query = query.Where(e => e.SubmittedAt >= desde);
        }
        if (request.Hasta is { } hasta)
        {
            query = query.Where(e => e.SubmittedAt <= hasta);
        }
        if (!string.IsNullOrWhiteSpace(request.QuoteReference))
        {
            query = query.Where(e => e.ReferenciaExterna == request.QuoteReference);
        }
        if (!string.IsNullOrWhiteSpace(request.QuoteReferenceSearch)
            && request.QuoteReferenceSearch.Length >= MinSearchLength)
        {
            // EF.Functions.ILike es Postgres-specific (case-insensitive).
            var pattern = $"%{request.QuoteReferenceSearch}%";
            query = query.Where(e => EF.Functions.ILike(e.ReferenciaExterna, pattern));
        }
        if (request.AwDocId is { } awDocId)
        {
            query = query.Where(e => e.AwDocId == awDocId);
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.SubmittedAt)
            .ThenBy(e => e.Id) // tiebreaker estable para offset paging
            .Skip(offset)
            .Take(limit)
            .Select(e => new CotizacionResumenItem(
                e.Id,
                e.ReferenciaExterna,
                e.Sucursal,
                e.Estado,
                e.SubmittedAt,
                e.DeliveredToAwAt,
                e.CorrelatedAt,
                e.AwDocId,
                e.RetryCount,
                e.LastError))
            .ToListAsync(cancellationToken);

        return new PagedResponse<CotizacionResumenItem>(items, offset, limit, total);
    }
}
