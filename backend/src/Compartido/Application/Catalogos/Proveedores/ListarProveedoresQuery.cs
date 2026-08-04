using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Proveedores;

/// <summary>
/// Lista paginada enriquecida de proveedores (F-Admin-PR4.5). Filtros
/// avanzados respecto al endpoint legacy de F7-PR1: rfc (substring),
/// razonSocial (substring), tipoPersona, estatus. La query original en
/// CatalogosEndpoints solo soporta <c>clave</c>+<c>estatus</c>.
/// </summary>
public sealed record ListarProveedoresQuery(
    string? Rfc = null,
    string? RazonSocial = null,
    TipoPersonaProveedor? TipoPersona = null,
    EstatusCatalogo? Estatus = null,
    int Offset = 0,
    int Limit = 50) : IRequest<ListarProveedoresResponse>;

public sealed record ProveedorItem(
    Guid Id,
    string Clave,
    string RazonSocial,
    string? NombreComercial,
    string Rfc,
    TipoPersonaProveedor TipoPersona,
    short? CondicionesPagoDias,
    Guid? MonedaPreferidaId,
    EstatusCatalogo Estatus);

public sealed record ListarProveedoresResponse(
    IReadOnlyList<ProveedorItem> Items,
    int Offset,
    int Limit,
    int Total);

public sealed class ListarProveedoresHandler
    : IRequestHandler<ListarProveedoresQuery, ListarProveedoresResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarProveedoresHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarProveedoresResponse> Handle(
        ListarProveedoresQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        IQueryable<Proveedor> q = _db.Proveedores.AsNoTracking();
        // Rfc = código → case-insensitive con lower() en ambos lados. RazonSocial
        // = texto libre → folding case + acentos (ADR-0045), igual que el nombre
        // de artículo, reusando el mapa centralizado en PostgresFunctions.
#pragma warning disable CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(query.Rfc))
            q = q.Where(p => p.Rfc.ToLower().Contains(query.Rfc.ToLower()));
        if (!string.IsNullOrWhiteSpace(query.RazonSocial))
        {
            var aguja = query.RazonSocial;
            q = q.Where(p =>
                PostgresFunctions.Translate(p.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
        }
#pragma warning restore CA1304, CA1311, CA1862
        if (query.TipoPersona is TipoPersonaProveedor tp)
            q = q.Where(p => p.TipoPersona == tp);
        if (query.Estatus is EstatusCatalogo e)
            q = q.Where(p => p.Estatus == e);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(p => p.Clave)
            .Skip(offset).Take(limit)
            .Select(p => new ProveedorItem(
                p.Id, p.Clave, p.RazonSocial, p.NombreComercial, p.Rfc,
                p.TipoPersona, p.CondicionesPagoDias, p.MonedaPreferidaId, p.Estatus))
            .ToListAsync(cancellationToken);

        return new ListarProveedoresResponse(items, offset, limit, total);
    }
}
