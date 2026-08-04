using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.Queries;

public sealed record ListarComprobacionesGastosQuery(
    TipoComprobacionGastos? Tipo = null,
    EstadoComprobacionGastos? Estado = null,
    Guid? SucursalId = null,
    Guid? ResponsableId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<ComprobacionGastosListItemResponse>>;

public sealed record ComprobacionGastosListItemResponse(
    Guid Id,
    TipoComprobacionGastos Tipo,
    EstadoComprobacionGastos Estado,
    Guid SucursalId,
    Guid ResponsableId,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    decimal MontoTotal,
    string Moneda,
    int NumeroLineas,
    DateTimeOffset FechaCreacion,
    int Version);

public sealed class ListarComprobacionesGastosHandler
    : IRequestHandler<ListarComprobacionesGastosQuery, PagedResponse<ComprobacionGastosListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarComprobacionesGastosHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<ComprobacionGastosListItemResponse>> Handle(
        ListarComprobacionesGastosQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.ComprobacionesGastos.AsNoTracking();
        if (query.Tipo is TipoComprobacionGastos t) q = q.Where(c => c.Tipo == t);
        if (query.Estado is EstadoComprobacionGastos e) q = q.Where(c => c.Estado == e);
        if (query.SucursalId is Guid s) q = q.Where(c => c.SucursalId == s);
        if (query.ResponsableId is Guid r) q = q.Where(c => c.ResponsableId == r);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(c => c.FechaCreacion)
            .Skip(offset)
            .Take(limit)
            .Select(c => new ComprobacionGastosListItemResponse(
                c.Id,
                c.Tipo,
                c.Estado,
                c.SucursalId,
                c.ResponsableId,
                c.FechaInicio,
                c.FechaFin,
                c.MontoTotal,
                c.Moneda,
                c.Lineas.Count(),
                c.FechaCreacion,
                c.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<ComprobacionGastosListItemResponse>(items, offset, limit, total);
    }
}
