using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Pedidos;

/// <summary>
/// FAC-08 (M8): el pedido facturado por parcialidades pasa a Facturado.
/// </summary>
public static class PedidoPorParcialidades
{
    private static readonly EstadoTimbrado[] Vigentes = [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente];

    /// <summary>
    /// Con la factura timbrada de una parcialidad, el pedido pasa a Facturado si sus salidas no
    /// descartadas cubren todas las posiciones y cada una tiene factura vigente. La factura que lo
    /// completa queda como comprobante vigente del pedido; al cancelarse cualquiera de ellas el
    /// pedido vuelve a Importado (efectos de cancelación existentes). Devuelve true si lo marcó.
    /// </summary>
    public static async Task<bool> TomarPedidoSiCompletoAsync(
        FacturacionDbContext db, FacturaVenta factura, PedidoFacturable? pedido, CancellationToken cancellationToken)
    {
        if (factura.SalidaFacturableId is not Guid salidaId || factura.Estado != EstadoTimbrado.Timbrado
            || factura.PedidoFacturableId is not Guid pedidoId)
            return false;

        pedido ??= await db.PedidosFacturables.Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken);
        if (pedido is null || pedido.Id != pedidoId || pedido.Estado != EstadoPedidoFacturable.Importado)
            return false;
        if (pedido.Lineas.Count == 0)
            await db.Entry(pedido).Collection(p => p.Lineas).LoadAsync(cancellationToken);

        var salidas = await db.SalidasFacturables.AsNoTracking().Include(s => s.Lineas)
            .Where(s => s.PedidoFacturableId == pedidoId && s.DescartadaEn == null)
            .ToListAsync(cancellationToken);
        var ids = salidas.Select(s => s.Id).ToList();
        var facturadas = (await db.FacturasVenta.AsNoTracking()
                .Where(f => f.SalidaFacturableId != null && ids.Contains(f.SalidaFacturableId.Value) && Vigentes.Contains(f.Estado))
                .Select(f => f.SalidaFacturableId!.Value)
                .ToListAsync(cancellationToken))
            .Append(salidaId)
            .ToHashSet();
        if (salidas.Any(s => !facturadas.Contains(s.Id)))
            return false;

        var enSalidas = salidas.SelectMany(s => s.Lineas).GroupBy(l => l.Posicion)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Cantidad));
        if (pedido.Lineas.Any(l => enSalidas.GetValueOrDefault(l.Posicion) < l.Cantidad))
            return false;

        pedido.MarcarFacturado(factura.Id);
        if (pedido.Origen == OrigenPedido.Aw)
        {
            var control = await db.IngestaControles
                .FirstOrDefaultAsync(c => c.PedidoFacturableId == pedido.Id, cancellationToken);
            control?.CambiarEstado(EstadoIngesta.Facturado);
        }
        return true;
    }
}
