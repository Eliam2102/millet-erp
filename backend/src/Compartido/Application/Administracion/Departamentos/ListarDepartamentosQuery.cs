using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Departamentos;

public sealed record ListarDepartamentosQuery(
    int Offset = 0,
    int Limit = 50,
    string? Query = null,
    Millet.Catalogos.Domain.EstatusCatalogo? Estatus = null)
    : IRequest<ListarDepartamentosResponse>;

public sealed record ListarDepartamentosResponse(
    IReadOnlyList<DepartamentoResponse> Items,
    int Total,
    int Offset,
    int Limit);

public sealed class ListarDepartamentosHandler
    : IRequestHandler<ListarDepartamentosQuery, ListarDepartamentosResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarDepartamentosHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarDepartamentosResponse> Handle(
        ListarDepartamentosQuery request,
        CancellationToken cancellationToken)
    {
        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit is <= 0 or > LimitMax ? 50 : request.Limit;
        var query = _db.Departamentos.AsNoTracking();

        if (request.Estatus is { } estatus)
        {
            query = query.Where(d => d.Estatus == estatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var text = request.Query.Trim();
            query = query.Where(d => d.Clave.Contains(text) || d.Nombre.Contains(text));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(d => d.Clave)
            .Skip(offset)
            .Take(limit)
            .Select(d => new DepartamentoResponse(
                d.Id, d.Clave, d.Nombre, d.Estatus, d.Version))
            .ToListAsync(cancellationToken);

        return new ListarDepartamentosResponse(items, total, offset, limit);
    }
}
