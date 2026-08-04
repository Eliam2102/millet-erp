using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Lista paginada de clientes (ADR-0048 D6). Filtros: rfc (substring),
/// razonSocial (substring con folding de acentos ADR-0045), origen,
/// estatus, y <c>fiscalesIncompletos</c> (bandeja de trabajo: clientes
/// auto-provisionados que aún no pueden timbrar).
/// </summary>
public sealed record ListarClientesQuery(
    string? Rfc = null,
    string? RazonSocial = null,
    OrigenMaster? Origen = null,
    EstatusCatalogo? Estatus = null,
    bool? FiscalesIncompletos = null,
    int Offset = 0,
    int Limit = 50) : IRequest<ListarClientesResponse>;

public sealed record ClienteItem(
    Guid Id,
    string Clave,
    string? ReferenciaExterna,
    string RazonSocial,
    string? Rfc,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string MonedaDefault,
    bool EsGenerico,
    OrigenMaster Origen,
    bool DatosFiscalesCompletos,
    EstatusCatalogo Estatus);

public sealed record ListarClientesResponse(
    IReadOnlyList<ClienteItem> Items,
    int Offset,
    int Limit,
    int Total);

public sealed class ListarClientesHandler
    : IRequestHandler<ListarClientesQuery, ListarClientesResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarClientesHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarClientesResponse> Handle(
        ListarClientesQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        IQueryable<Cliente> q = _db.Clientes.AsNoTracking();
#pragma warning disable CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(query.Rfc))
            q = q.Where(c => c.Rfc != null && c.Rfc.ToLower().Contains(query.Rfc.ToLower()));
        if (!string.IsNullOrWhiteSpace(query.RazonSocial))
        {
            var aguja = query.RazonSocial;
            q = q.Where(c =>
                PostgresFunctions.Translate(c.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(aguja.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
        }
#pragma warning restore CA1304, CA1311, CA1862
        if (query.Origen is OrigenMaster o)
            q = q.Where(c => c.Origen == o);
        if (query.Estatus is EstatusCatalogo e)
            q = q.Where(c => c.Estatus == e);
        if (query.FiscalesIncompletos is bool fi)
        {
            q = fi
                ? q.Where(c => c.Rfc == null || c.RegimenFiscal == null || c.CodigoPostalFiscal == null)
                : q.Where(c => c.Rfc != null && c.RegimenFiscal != null && c.CodigoPostalFiscal != null);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(c => c.Clave)
            .Skip(offset).Take(limit)
            .Select(c => new ClienteItem(
                c.Id, c.Clave, c.ReferenciaExterna, c.RazonSocial, c.Rfc,
                c.RegimenFiscal, c.CodigoPostalFiscal, c.MonedaDefault,
                c.EsGenerico, c.Origen,
                c.Rfc != null && c.RegimenFiscal != null && c.CodigoPostalFiscal != null,
                c.Estatus))
            .ToListAsync(cancellationToken);

        return new ListarClientesResponse(items, offset, limit, total);
    }
}
