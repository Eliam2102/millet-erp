using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Articulos;

/// <summary>
/// Lista paginada enriquecida de artículos (F-Admin-PR4.5). Filtros
/// avanzados respecto al endpoint legacy de F7-PR1: codigo (substring
/// sobre clave), descripcion (substring sobre nombre), naturaleza,
/// unidadMedidaDefault, estatus.
/// </summary>
public sealed record ListarArticulosQuery(
    string? Codigo = null,
    string? Descripcion = null,
    Naturaleza? Naturaleza = null,
    string? UnidadMedidaDefault = null,
    EstatusCatalogo? Estatus = null,
    int Offset = 0,
    int Limit = 50) : IRequest<ListarArticulosResponse>;

public sealed record ArticuloItem(
    Guid Id,
    string Clave,
    string Nombre,
    string UnidadMedidaDefault,
    Guid? UnidadMedidaId,
    Naturaleza Naturaleza,
    string? Categoria,
    Guid? CategoriaId,
    EstatusCatalogo Estatus);

public sealed record ListarArticulosResponse(
    IReadOnlyList<ArticuloItem> Items,
    int Offset,
    int Limit,
    int Total);

public sealed class ListarArticulosHandler
    : IRequestHandler<ListarArticulosQuery, ListarArticulosResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarArticulosHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarArticulosResponse> Handle(
        ListarArticulosQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        IQueryable<Articulo> q = _db.Articulos.AsNoTracking();
        // Codigo (clave) = código → case-insensitive con lower() en ambos lados.
        // Descripcion (nombre) = texto libre → folding case + acentos (ADR-0045),
        // reusando el mapa centralizado en PostgresFunctions.
#pragma warning disable CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(query.Codigo))
            q = q.Where(a => a.Clave.ToLower().Contains(query.Codigo.ToLower()));
        if (!string.IsNullOrWhiteSpace(query.Descripcion))
        {
            var aguja = query.Descripcion;
            q = q.Where(a =>
                PostgresFunctions.Translate(a.Nombre.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
        }
#pragma warning restore CA1304, CA1311, CA1862
        if (query.Naturaleza is Naturaleza n)
            q = q.Where(a => a.Naturaleza == n);
        if (!string.IsNullOrWhiteSpace(query.UnidadMedidaDefault))
            q = q.Where(a => a.UnidadMedidaDefault == query.UnidadMedidaDefault);
        if (query.Estatus is EstatusCatalogo e)
            q = q.Where(a => a.Estatus == e);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(a => a.Clave)
            .Skip(offset).Take(limit)
            .Select(a => new ArticuloItem(
                a.Id, a.Clave, a.Nombre, a.UnidadMedidaDefault,
                a.UnidadMedidaId, a.Naturaleza, a.Categoria, a.CategoriaId, a.Estatus))
            .ToListAsync(cancellationToken);

        return new ListarArticulosResponse(items, offset, limit, total);
    }
}
