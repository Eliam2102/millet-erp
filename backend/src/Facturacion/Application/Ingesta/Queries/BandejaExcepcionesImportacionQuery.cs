using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Ingesta.Queries;

/// <summary>Bandeja de excepciones de importación (§12.1), filtrable por pendientes.</summary>
public sealed record BandejaExcepcionesImportacionQuery(bool SoloPendientes, int Offset, int Limit, string? Q = null,
    bool SoloResueltas = false) : IRequest<BandejaExcepcionesImportacionResponse>;

public sealed record BandejaExcepcionesImportacionResponse(IReadOnlyList<ExcepcionImportacionItem> Items, int Total = 0);

public sealed record ExcepcionImportacionItem(
    Guid Id,
    string Origen,
    string PedidoRef,
    string Motivo,
    string? Detalle,
    bool Resuelto,
    DateTimeOffset CreatedAt);

public sealed class BandejaExcepcionesImportacionHandler
    : IRequestHandler<BandejaExcepcionesImportacionQuery, BandejaExcepcionesImportacionResponse>
{
    private readonly FacturacionDbContext _db;

    public BandejaExcepcionesImportacionHandler(FacturacionDbContext db) => _db = db;

    public async Task<BandejaExcepcionesImportacionResponse> Handle(
        BandejaExcepcionesImportacionQuery query,
        CancellationToken cancellationToken)
    {
        var q = _db.ExcepcionesImportacion.AsNoTracking();
        if (query.SoloPendientes) q = q.Where(e => !e.Resuelto);
        else if (query.SoloResueltas) q = q.Where(e => e.Resuelto);
        if (query.Q?.Trim() is { Length: > 0 } texto)
        {
            var patron = PaginacionExtensions.PatronContiene(texto);
            q = q.Where(e => EF.Functions.ILike(e.PedidoRef, patron)
                          || (e.Detalle != null && EF.Functions.ILike(e.Detalle, patron)));
        }

        var pagina = await q
            .OrderByDescending(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.Origen, e.PedidoRef, e.Motivo, e.Detalle, e.Resuelto, e.CreatedAt })
            .ToPaginaAsync(query.Offset, query.Limit, cancellationToken);

        var items = pagina.Items
            .Select(r => new ExcepcionImportacionItem(
                r.Id, r.Origen.ToString(), r.PedidoRef, r.Motivo.ToString(), r.Detalle, r.Resuelto, r.CreatedAt))
            .ToList();
        return new BandejaExcepcionesImportacionResponse(items, pagina.Total);
    }
}
