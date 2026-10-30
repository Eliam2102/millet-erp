using Millet.ProductoTerminado.Domain;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application.Ports;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;
public sealed class PedidosPtReadAdapter(FacturacionDbContext db) : IPedidosPtReadPort
{
    public async Task<PtOrderSource?> GetAsync(Guid empresaId, string orderNumber, CancellationToken ct)
    {
        var p = await db.PedidosFacturables.AsNoTracking().Include(x => x.Lineas)
            .SingleOrDefaultAsync(x => x.EmpresaId == empresaId && x.Origen == OrigenPedido.Aw && x.NumeroPedido == orderNumber, ct);
        return p is null ? null : new(p.Id, p.NumeroPedido!, p.SucursalId,
            p.Estado == EstadoPedidoFacturable.Cancelado ? "Cancelado" : p.EstadoOrigen ?? p.Estado.ToString(),
            p.CondicionPagoOrigen, p.ClienteId, p.Moneda, p.Total, p.VersionOrigen,
            p.Lineas.Select(l => new PtOrderLineSource(l.Posicion, l.ProductoId, l.ProductoDescripcion, l.Cantidad, l.ClaveUnidadSat)).ToArray());
    }
}
