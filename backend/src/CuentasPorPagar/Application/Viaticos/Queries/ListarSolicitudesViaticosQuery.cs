using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Viaticos.Queries;

public sealed record ListarSolicitudesViaticosQuery(
    EstadoSolicitudViaticos? Estado = null,
    Guid? EmpleadoId = null,
    Guid? JefeDirectoId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<SolicitudViaticosListItemResponse>>;

public sealed record SolicitudViaticosListItemResponse(
    Guid Id,
    Guid EmpleadoId,
    Guid JefeDirectoId,
    string Destino,
    DateOnly FechaSalida,
    DateOnly FechaRegreso,
    decimal MontoSolicitado,
    decimal TopePolitica,
    bool ExcedePolitica,
    EstadoSolicitudViaticos Estado,
    decimal? MontoComprobado,
    decimal? DiferenciaLiquidacion,
    DateTimeOffset FechaSolicitud,
    int Version);

public sealed class ListarSolicitudesViaticosHandler
    : IRequestHandler<ListarSolicitudesViaticosQuery, PagedResponse<SolicitudViaticosListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarSolicitudesViaticosHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<SolicitudViaticosListItemResponse>> Handle(
        ListarSolicitudesViaticosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.SolicitudesViaticos.AsNoTracking();
        if (query.Estado is EstadoSolicitudViaticos e) q = q.Where(s => s.Estado == e);
        if (query.EmpleadoId is Guid emp) q = q.Where(s => s.EmpleadoId == emp);
        if (query.JefeDirectoId is Guid jefe) q = q.Where(s => s.JefeDirectoId == jefe);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(s => s.FechaSolicitud)
            .Skip(offset)
            .Take(limit)
            .Select(s => new SolicitudViaticosListItemResponse(
                s.Id, s.EmpleadoId, s.JefeDirectoId,
                s.Destino, s.FechaSalida, s.FechaRegreso,
                s.MontoSolicitado, s.TopePoliticaSnapshot, s.ExcedePolitica,
                s.Estado, s.MontoComprobado, s.DiferenciaLiquidacion,
                s.FechaSolicitud, s.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<SolicitudViaticosListItemResponse>(items, offset, limit, total);
    }
}
