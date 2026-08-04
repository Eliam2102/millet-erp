using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Pedidos.Queries;

/// <summary>
/// Historial de comprobantes de un pedido (B5, FE-F2): todas las facturas de
/// venta emitidas contra el pedido a lo largo del tiempo (canceladas + la
/// vigente). Un pedido acumula N comprobantes; el vigente es el no cancelado
/// actual (invariante 5).
/// </summary>
public sealed record PedidoComprobantesQuery(Guid PedidoFacturableId) : IRequest<IReadOnlyList<PedidoComprobanteItem>>;

public sealed record PedidoComprobanteItem(
    Guid Id,
    string Folio,
    string Estado,
    string? Uuid,
    decimal Total,
    string Moneda,
    DateTimeOffset? FechaTimbrado,
    bool Vigente);

public sealed class PedidoComprobantesHandler
    : IRequestHandler<PedidoComprobantesQuery, IReadOnlyList<PedidoComprobanteItem>>
{
    private readonly FacturacionDbContext _db;

    public PedidoComprobantesHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<PedidoComprobanteItem>> Handle(
        PedidoComprobantesQuery query, CancellationToken cancellationToken)
    {
        var vigenteId = await _db.PedidosFacturables.AsNoTracking()
            .Where(p => p.Id == query.PedidoFacturableId)
            .Select(p => p.ComprobanteVigenteId)
            .FirstOrDefaultAsync(cancellationToken);

        var rows = await _db.FacturasVenta.AsNoTracking()
            .Where(f => f.PedidoFacturableId == query.PedidoFacturableId)
            .OrderByDescending(f => f.FolioNumero)
            .Select(f => new { f.Id, f.Folio, f.Estado, f.Uuid, f.Total, f.Moneda, f.FechaTimbrado })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new PedidoComprobanteItem(
                r.Id, r.Folio, r.Estado.ToString(), r.Uuid, r.Total, r.Moneda, r.FechaTimbrado,
                vigenteId is not null && r.Id == vigenteId))
            .ToList();
    }
}
