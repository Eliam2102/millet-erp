using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;
public sealed class PedidosEmbarcablesReadAdapter(FacturacionDbContext db) : IPedidosEmbarcablesReadPort
{
    public async Task<IReadOnlyList<PedidoEmbarcable>> ListarAsync(Guid clienteId, Guid sucursalId, CancellationToken ct) =>
        await LeerAsync(db.PedidosFacturables.Where(p => p.ClienteId == clienteId && p.SucursalId == sucursalId &&
            p.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce && p.Estado == EstadoPedidoFacturable.Importado), ct);
    public Task<IReadOnlyList<PedidoEmbarcable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        LeerAsync(db.PedidosFacturables.Where(p => ids.Contains(p.Id)), ct);
    private async Task<IReadOnlyList<PedidoEmbarcable>> LeerAsync(IQueryable<PedidoFacturable> q, CancellationToken ct)
    {
        var pedidos = await q.AsNoTracking().Include(p => p.Lineas).OrderBy(p => p.NumeroPedido).ToListAsync(ct);
        var facturasIds = pedidos.Where(p => p.ComprobanteVigenteId != null).Select(p => p.ComprobanteVigenteId!.Value).ToArray();
        var facturas = await db.FacturasVenta.AsNoTracking().Where(f => facturasIds.Contains(f.Id) && f.Estado != EstadoTimbrado.Cancelado && f.Estado != EstadoTimbrado.Descartada)
            .Select(f => new { f.Id, f.Folio, f.Uuid }).ToDictionaryAsync(f => f.Id, ct);
        return pedidos.Select(p => new PedidoEmbarcable(p.Id, p.NumeroPedido ?? "[PEDIDO]", p.ClienteId, p.ClienteNombre,
            p.SucursalId, p.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce, p.Estado.ToString(), p.VersionOrigen,
            p.Lineas.OrderBy(l => l.Posicion).Select(l => new LineaEmbarcable(l.Posicion, l.ProductoId, l.ProductoDescripcion, l.Cantidad, l.ClaveUnidadSat ?? "")).ToArray(),
            p.ComprobanteVigenteId is Guid fid ? facturas.GetValueOrDefault(fid)?.Folio : null,
            p.ComprobanteVigenteId is Guid uuid ? facturas.GetValueOrDefault(uuid)?.Uuid : null,
            p.ComprobanteVigenteId is Guid vigente && facturas.ContainsKey(vigente))).ToArray();
    }
}
