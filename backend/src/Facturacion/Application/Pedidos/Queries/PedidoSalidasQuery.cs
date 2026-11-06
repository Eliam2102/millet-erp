using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos.Queries;

/// <summary>
/// FAC-08: salidas parciales de un pedido con su factura vigente y el avance por posición
/// (pedido, en salidas y facturado). El pedido original no cambia: el avance se calcula.
/// </summary>
public sealed record PedidoSalidasQuery(Guid PedidoFacturableId) : IRequest<PedidoSalidasResponse>;

public sealed record PedidoSalidasResponse(
    Guid PedidoFacturableId, decimal TotalPedido, decimal TotalFacturado,
    IReadOnlyList<AvancePosicionPedido> Avance, IReadOnlyList<SalidaFacturableItem> Salidas);

public sealed record AvancePosicionPedido(int Posicion, string Descripcion, decimal Pedida, decimal EnSalidas, decimal Facturada);

/// <summary>
/// Factura vigente de la salida (null si está pendiente de facturar o se canceló). <c>DescartadaEn</c>: la salida se
/// descartó porque su origen se canceló (contenedor cancelado en CE) y ya no se factura.
/// </summary>
public sealed record SalidaFacturableItem(
    Guid Id, int NumeroParcialidad, OrigenSalidaFacturable Origen, string Referencia, DateTimeOffset RegistradaEn,
    decimal Total, Guid? FacturaId, string? Folio, string? Uuid, string? EstadoFactura,
    IReadOnlyList<SalidaFacturableItemLinea> Lineas, DateTimeOffset? DescartadaEn = null, string? MotivoDescarte = null);

public sealed record SalidaFacturableItemLinea(int Posicion, decimal Cantidad, decimal Importe);

public sealed class PedidoSalidasHandler(FacturacionDbContext db) : IRequestHandler<PedidoSalidasQuery, PedidoSalidasResponse>
{
    public async Task<PedidoSalidasResponse> Handle(PedidoSalidasQuery query, CancellationToken cancellationToken)
    {
        var pedido = await db.PedidosFacturables.AsNoTracking().Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.Id == query.PedidoFacturableId, cancellationToken)
            ?? throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable {query.PedidoFacturableId}.");

        var salidas = await db.SalidasFacturables.AsNoTracking().Include(s => s.Lineas)
            .Where(s => s.PedidoFacturableId == pedido.Id)
            .OrderBy(s => s.NumeroParcialidad)
            .ToListAsync(cancellationToken);
        var ids = salidas.Select(s => s.Id).ToList();
        var facturas = await db.FacturasVenta.AsNoTracking()
            .Where(f => f.SalidaFacturableId != null && ids.Contains(f.SalidaFacturableId.Value)
                && f.Estado != EstadoTimbrado.Cancelado && f.Estado != EstadoTimbrado.Descartada)
            .Select(f => new { f.Id, SalidaId = f.SalidaFacturableId!.Value, f.Folio, f.Uuid, f.Estado })
            .ToListAsync(cancellationToken);
        var facturaDe = facturas.ToDictionary(f => f.SalidaId);

        var items = salidas.Select(s =>
        {
            var f = facturaDe.GetValueOrDefault(s.Id);
            return new SalidaFacturableItem(s.Id, s.NumeroParcialidad, s.Origen, s.Referencia, s.RegistradaEn, s.Total,
                f?.Id, f?.Folio, f?.Uuid, f?.Estado.ToString(),
                s.Lineas.OrderBy(l => l.Posicion).Select(l => new SalidaFacturableItemLinea(l.Posicion, l.Cantidad, l.Importe)).ToList(),
                s.DescartadaEn, s.MotivoDescarte);
        }).ToList();

        var facturadas = salidas.Where(s => facturaDe.ContainsKey(s.Id)).SelectMany(s => s.Lineas).ToList();
        var enSalidas = salidas.Where(s => !s.Descartada).SelectMany(s => s.Lineas).ToList();
        var avance = pedido.Lineas.OrderBy(l => l.Posicion).Select(l => new AvancePosicionPedido(
            l.Posicion, l.ProductoDescripcion, l.Cantidad,
            enSalidas.Where(x => x.Posicion == l.Posicion).Sum(x => x.Cantidad),
            facturadas.Where(x => x.Posicion == l.Posicion).Sum(x => x.Cantidad))).ToList();

        return new PedidoSalidasResponse(pedido.Id, pedido.Total, facturadas.Sum(l => l.Importe), avance, items);
    }
}
