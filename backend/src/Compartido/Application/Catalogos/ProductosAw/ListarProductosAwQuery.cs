using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.ProductosAw;

/// <summary>
/// Lista paginada de productos A+W (ADR-0048 D5). Filtros: referencia
/// (substring), descripcion (substring con folding de acentos ADR-0045),
/// origen, estatus y <c>fiscalesIncompletos</c> (bandeja de trabajo:
/// productos sin claves SAT que bloquean timbrado).
/// </summary>
public sealed record ListarProductosAwQuery(
    string? Referencia = null,
    string? Descripcion = null,
    OrigenMaster? Origen = null,
    EstatusCatalogo? Estatus = null,
    bool? FiscalesIncompletos = null,
    int Offset = 0,
    int Limit = 50) : IRequest<ListarProductosAwResponse>;

public sealed record ProductoAwItem(
    Guid Id,
    string ReferenciaExterna,
    string Descripcion,
    string UnidadMedida,
    Guid? UnidadMedidaId,
    Guid? CategoriaId,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    string? FraccionArancelaria,
    string? UnidadAduana,
    decimal? PesoUnitarioKg,
    OrigenMaster Origen,
    bool DatosFiscalesCompletos,
    EstatusCatalogo Estatus);

public sealed record ListarProductosAwResponse(
    IReadOnlyList<ProductoAwItem> Items,
    int Offset,
    int Limit,
    int Total);

public sealed class ListarProductosAwHandler
    : IRequestHandler<ListarProductosAwQuery, ListarProductosAwResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarProductosAwHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarProductosAwResponse> Handle(
        ListarProductosAwQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        IQueryable<ProductoAw> q = _db.ProductosAw.AsNoTracking();
#pragma warning disable CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(query.Referencia))
            q = q.Where(p => p.ReferenciaExterna.ToLower().Contains(query.Referencia.ToLower()));
        if (!string.IsNullOrWhiteSpace(query.Descripcion))
        {
            var aguja = query.Descripcion;
            q = q.Where(p =>
                PostgresFunctions.Translate(p.Descripcion.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
        }
#pragma warning restore CA1304, CA1311, CA1862
        if (query.Origen is OrigenMaster o)
            q = q.Where(p => p.Origen == o);
        if (query.Estatus is EstatusCatalogo e)
            q = q.Where(p => p.Estatus == e);
        if (query.FiscalesIncompletos is bool fi)
        {
            q = fi
                ? q.Where(p => p.ClaveProdServSat == null || p.ClaveUnidadSat == null)
                : q.Where(p => p.ClaveProdServSat != null && p.ClaveUnidadSat != null);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(p => p.ReferenciaExterna)
            .Skip(offset).Take(limit)
            .Select(p => new ProductoAwItem(
                p.Id, p.ReferenciaExterna, p.Descripcion, p.UnidadMedida,
                p.UnidadMedidaId, p.CategoriaId, p.ClaveProdServSat,
                p.ClaveUnidadSat, p.ObjetoImp, p.TasaIvaTraslado,
                p.FraccionArancelaria, p.UnidadAduana, p.PesoUnitarioKg, p.Origen,
                p.ClaveProdServSat != null && p.ClaveUnidadSat != null,
                p.Estatus))
            .ToListAsync(cancellationToken);

        return new ListarProductosAwResponse(items, offset, limit, total);
    }
}
