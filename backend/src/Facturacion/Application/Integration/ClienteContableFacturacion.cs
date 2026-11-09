using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Integration;

/// <summary>
/// CXC-PR3: el comprobante conserva el RFC. Para los eventos contables se
/// infiere el cliente desde pedidos y anticipos del propio módulo; sin
/// una referencia local disponible se publica null, sin consultas por RFC.
/// </summary>
internal static class ClienteContableFacturacion
{
    public static async Task<Guid?> ResolverAsync(
        FacturacionDbContext db, Comprobante comprobante, CancellationToken ct)
    {
        if (comprobante is FacturaAnticipo anticipo)
            return await DesdeAnticipoAsync(db, anticipo.AnticipoId, ct);

        if (comprobante is FacturaVenta factura)
            return await DesdePedidoAsync(db, factura.PedidoFacturableId, ct);

        if (comprobante is NotaCredito nc)
        {
            if (nc.AnticipoOrigenId is Guid anticipoId)
            {
                var cliente = await DesdeAnticipoAsync(db, anticipoId, ct);
                if (cliente is not null) return cliente;
            }
            return await (from f in db.FacturasVenta.AsNoTracking()
                          join p in db.PedidosFacturables.AsNoTracking() on f.PedidoFacturableId equals (Guid?)p.Id
                          where f.Id == nc.FacturaRelacionadaId
                          select (Guid?)p.ClienteId).FirstOrDefaultAsync(ct);
        }

        // B11: el REPP no tiene pedido propio; su cliente es el de las facturas que paga (todas del mismo
        // receptor). Sin él, Contabilidad esperaba una póliza del REPP que nunca nace.
        if (comprobante is ReciboPago repp)
        {
            var facturas = repp.FacturasPagadas.Count > 0
                ? repp.FacturasPagadas.Select(f => f.FacturaVentaId).ToList()
                : await db.RecibosPago.AsNoTracking().Where(r => r.Id == repp.Id)
                    .SelectMany(r => r.FacturasPagadas.Select(f => f.FacturaVentaId)).ToListAsync(ct);
            return await (from f in db.FacturasVenta.AsNoTracking()
                          join p in db.PedidosFacturables.AsNoTracking() on f.PedidoFacturableId equals (Guid?)p.Id
                          where facturas.Contains(f.Id)
                          select (Guid?)p.ClienteId).FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private static Task<Guid?> DesdePedidoAsync(FacturacionDbContext db, Guid? id, CancellationToken ct) =>
        db.PedidosFacturables.AsNoTracking().Where(p => p.Id == id)
            .Select(p => (Guid?)p.ClienteId).FirstOrDefaultAsync(ct);

    private static Task<Guid?> DesdeAnticipoAsync(FacturacionDbContext db, Guid id, CancellationToken ct) =>
        db.Anticipos.AsNoTracking().Where(a => a.Id == id)
            .Select(a => (Guid?)a.ClienteId).FirstOrDefaultAsync(ct);
}
