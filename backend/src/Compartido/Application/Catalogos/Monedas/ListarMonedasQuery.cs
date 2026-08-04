using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Catalogos.Application.Monedas;

/// <summary>
/// Lista de monedas (F-Admin-PR5.1). Sin paginación porque el catálogo
/// es pequeño (12 entries seed + posibles adiciones).
/// </summary>
public sealed record ListarMonedasQuery(bool? SoloActivas = null)
    : IRequest<IReadOnlyList<MonedaResponse>>;

public sealed class ListarMonedasHandler
    : IRequestHandler<ListarMonedasQuery, IReadOnlyList<MonedaResponse>>
{
    private readonly CompartidoDbContext _db;

    public ListarMonedasHandler(CompartidoDbContext db) => _db = db;

    public async Task<IReadOnlyList<MonedaResponse>> Handle(
        ListarMonedasQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Moneda> q = _db.Monedas.AsNoTracking();
        if (query.SoloActivas is true) q = q.Where(m => m.Activa);

        return await q
            .OrderBy(m => m.Codigo)
            .Select(m => new MonedaResponse(
                m.Id, m.Codigo, m.Nombre, m.Decimales, m.Activa, m.Version))
            .ToListAsync(cancellationToken);
    }
}
