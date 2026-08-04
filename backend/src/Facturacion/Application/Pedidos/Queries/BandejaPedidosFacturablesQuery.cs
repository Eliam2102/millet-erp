using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Pedidos.Queries;

/// <summary>
/// Bandeja de pedidos facturables (§7.2 diseño), con filtros y paginación.
/// CAJAS-PR2: filtrada por la Capa A de Cajas; <paramref name="SoloSinAsignar"/>
/// restringe al bucket "Sin asignar" (requiere <c>facturacion.caja.leer-todas</c>).
/// </summary>
public sealed record BandejaPedidosFacturablesQuery(
    EstadoPedidoFacturable? Estado,
    OrigenPedido? Origen,
    int Offset,
    int Limit,
    bool SoloSinAsignar = false) : IRequest<BandejaPedidosFacturablesResponse>;

public sealed record BandejaPedidosFacturablesResponse(
    IReadOnlyList<PedidoBandejaItem> Items,
    int? SinAsignarCount);

public sealed record PedidoBandejaItem(
    Guid Id,
    string? NumeroPedido,
    string Origen,
    string Estado,
    string ClienteNombre,
    decimal Total,
    string Moneda,
    DateTimeOffset CreatedAt);

public sealed class BandejaPedidosFacturablesHandler
    : IRequestHandler<BandejaPedidosFacturablesQuery, BandejaPedidosFacturablesResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public BandejaPedidosFacturablesHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<BandejaPedidosFacturablesResponse> Handle(
        BandejaPedidosFacturablesQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);

        var q = _db.PedidosFacturables.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(p => p.Estado == estado);
        if (query.Origen is { } origen) q = q.Where(p => p.Origen == origen);

        (q, var sinAsignarCount) = await AlcanceBandejaHelper.AplicarAsync(
            _alcance, q, query.SoloSinAsignar, cancellationToken);

        var rows = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(p => new { p.Id, p.NumeroPedido, p.Origen, p.Estado, p.ClienteNombre, p.Total, p.Moneda, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new PedidoBandejaItem(
                r.Id, r.NumeroPedido, r.Origen.ToString(), r.Estado.ToString(),
                r.ClienteNombre, r.Total, r.Moneda, r.CreatedAt))
            .ToList();

        return new BandejaPedidosFacturablesResponse(items, sinAsignarCount);
    }
}
