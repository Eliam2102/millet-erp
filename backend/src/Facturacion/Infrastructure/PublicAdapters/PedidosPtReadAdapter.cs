using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application.Ports;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>Expone a Producto Terminado el pedido A+W ingerido, sin que PT toque tablas de Facturación.</summary>
public sealed class PedidosPtReadAdapter(FacturacionDbContext db) : IPedidosPtReadPort
{
    public async Task<PedidoPtOrigen?> ObtenerPorNumeroAsync(Guid empresaId, string numeroPedido, CancellationToken ct)
    {
        var numero = numeroPedido.Trim();
        var p = await db.PedidosFacturables.AsNoTracking().Include(x => x.Lineas)
            .Where(x => x.EmpresaId == empresaId && x.Origen == OrigenPedido.Aw && x.NumeroPedido == numero)
            .FirstOrDefaultAsync(ct);
        if (p is null) return null;
        return new PedidoPtOrigen(p.Id, numero, p.SucursalId, p.Estado.ToString(), p.CondicionPagoOrigen, p.VersionOrigen,
            p.Lineas.OrderBy(l => l.Posicion)
                .Select(l => new LineaPtOrigen(l.Posicion, l.ProductoId, l.ProductoDescripcion, l.Cantidad, l.ClaveUnidadSat))
                .ToArray(), p.ClienteId, p.Moneda, p.Total);
    }

    private static readonly string[] EstadosSalida = ["72", "115"];

    public async Task<IReadOnlyDictionary<Guid, PedidoMonitorOrigen>> ObtenerParaMonitorAsync(Guid empresaId, IReadOnlyCollection<Guid> pedidoFacturableIds, CancellationToken ct) =>
        await db.PedidosFacturables.AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && pedidoFacturableIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                Info = new PedidoMonitorOrigen(x.ClienteNombre, x.ObraNombre,
                    db.SalidasFacturables.Any(s => s.PedidoFacturableId == x.Id) || (x.EstadoOrigen != null && EstadosSalida.Contains(x.EstadoOrigen))),
            })
            .ToDictionaryAsync(x => x.Id, x => x.Info, ct);
}
