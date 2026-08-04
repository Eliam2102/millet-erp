using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Catalogos.Application.Monedas;

/// <summary>
/// Lista paginada de tipos de cambio para una moneda específica
/// (F-Admin-PR5.1). Ordenado por fecha descendente — el más reciente
/// primero. Tope 200; default 50.
/// </summary>
public sealed record ListarTiposCambioPorMonedaQuery(
    Guid MonedaId,
    int Offset = 0,
    int Limit = 50) : IRequest<ListarTiposCambioResponse>;

public sealed record ListarTiposCambioResponse(
    IReadOnlyList<TipoCambioResponse> Items,
    int Offset,
    int Limit,
    int Total);

public sealed class ListarTiposCambioHandler
    : IRequestHandler<ListarTiposCambioPorMonedaQuery, ListarTiposCambioResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarTiposCambioHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarTiposCambioResponse> Handle(
        ListarTiposCambioPorMonedaQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        var baseQuery = _db.TiposCambio.AsNoTracking()
            .Where(t => t.MonedaId == query.MonedaId);

        var total = await baseQuery.CountAsync(cancellationToken);
        var items = await baseQuery
            .OrderByDescending(t => t.Fecha)
            .Skip(offset).Take(limit)
            .Select(t => new TipoCambioResponse(
                t.Id, t.MonedaId, t.Fecha, t.ValorEnMxn, t.Origen, t.Version))
            .ToListAsync(cancellationToken);

        return new ListarTiposCambioResponse(items, offset, limit, total);
    }
}
