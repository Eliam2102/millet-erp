using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Ingesta.Queries;

/// <summary>Bandeja de excepciones de importación (§12.1), filtrable por pendientes.</summary>
public sealed record BandejaExcepcionesImportacionQuery(bool SoloPendientes, int Offset, int Limit)
    : IRequest<IReadOnlyList<ExcepcionImportacionItem>>;

public sealed record ExcepcionImportacionItem(
    Guid Id,
    string Origen,
    string PedidoRef,
    string Motivo,
    string? Detalle,
    bool Resuelto,
    DateTimeOffset CreatedAt);

public sealed class BandejaExcepcionesImportacionHandler
    : IRequestHandler<BandejaExcepcionesImportacionQuery, IReadOnlyList<ExcepcionImportacionItem>>
{
    private readonly FacturacionDbContext _db;

    public BandejaExcepcionesImportacionHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<ExcepcionImportacionItem>> Handle(
        BandejaExcepcionesImportacionQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);

        var q = _db.ExcepcionesImportacion.AsNoTracking();
        if (query.SoloPendientes) q = q.Where(e => !e.Resuelto);

        var rows = await q
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(e => new { e.Id, e.Origen, e.PedidoRef, e.Motivo, e.Detalle, e.Resuelto, e.CreatedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ExcepcionImportacionItem(
                r.Id, r.Origen.ToString(), r.PedidoRef, r.Motivo.ToString(), r.Detalle, r.Resuelto, r.CreatedAt))
            .ToList();
    }
}
