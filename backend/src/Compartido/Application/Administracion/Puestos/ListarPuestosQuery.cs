using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Puestos;

public sealed record ListarPuestosQuery(
    int Offset = 0,
    int Limit = 50,
    string? Query = null,
    EstatusCatalogo? Estatus = null)
    : IRequest<ListarPuestosResponse>;

public sealed record ListarPuestosResponse(
    IReadOnlyList<PuestoResponse> Items,
    int Total,
    int Offset,
    int Limit);

public sealed class ListarPuestosHandler
    : IRequestHandler<ListarPuestosQuery, ListarPuestosResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarPuestosHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarPuestosResponse> Handle(
        ListarPuestosQuery request,
        CancellationToken cancellationToken)
    {
        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit is <= 0 or > LimitMax ? 50 : request.Limit;
        var query = _db.Puestos.AsNoTracking();

        if (request.Estatus is { } estatus)
        {
            query = query.Where(p => p.Estatus == estatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var text = request.Query.Trim();
            query = query.Where(p => p.Clave.Contains(text) || p.Nombre.Contains(text));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Clave)
            .Skip(offset)
            .Take(limit)
            .Select(p => new PuestoResponse(
                p.Id, p.Clave, p.Nombre, p.Estatus, p.Version))
            .ToListAsync(cancellationToken);

        return new ListarPuestosResponse(items, total, offset, limit);
    }
}
