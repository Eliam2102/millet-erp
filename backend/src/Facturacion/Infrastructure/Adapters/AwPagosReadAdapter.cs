using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure;

/// <summary>
/// Pagos acumulados por factura A+W (cobros de mostrador activos + líneas de REPP timbrados), agrupados por
/// forma de pago SAT + moneda, para el retorno tipo 4. Lectura pura, para Integraciones.Aw.
/// </summary>
public sealed class AwPagosReadAdapter(FacturacionDbContext db, ICurrentEmpresaContext empresa) : IAwPagosReadPort
{
    private sealed record Factura(Guid Id, string Folio, string? Pedido, bool EsFinal);

    public async Task<IReadOnlyList<PagosFacturaAw>> PorComprobanteAsync(Guid comprobanteId, CancellationToken cancellationToken)
    {
        using var bypass = empresa.Bypass(); // corre en un worker, sin empresa de request
        var ids = new List<Guid> { comprobanteId };
        // REPP: las facturas que paga (un cobro cuyo ComprobanteId es el REPP no cuenta: se cuenta por sus líneas).
        ids.AddRange(await db.RecibosPago.AsNoTracking().Where(r => r.Id == comprobanteId)
            .SelectMany(r => r.FacturasPagadas.Select(l => l.FacturaVentaId))
            .ToListAsync(cancellationToken));
        var ventas = await Ventas(f => ids.Contains(f.Id), cancellationToken);
        var anticipos = await Anticipos(f => f.Id == comprobanteId, cancellationToken);
        return await Calcular(ventas.Concat(anticipos), cancellationToken);
    }

    public async Task<PagosFacturaAw?> PorFolioAsync(string numeroPedidoAw, string folio, CancellationToken cancellationToken)
    {
        using var bypass = empresa.Bypass();
        var ventas = await Ventas(f => f.Folio == folio, cancellationToken);
        var anticipos = await Anticipos(f => f.Folio == folio, cancellationToken);
        return (await Calcular(ventas.Concat(anticipos).Where(f => f.Pedido == numeroPedidoAw), cancellationToken))
            .FirstOrDefault();
    }

    private async Task<List<Factura>> Ventas(
        System.Linq.Expressions.Expression<Func<Domain.Facturas.FacturaVenta, bool>> filtro, CancellationToken ct) =>
        await (from f in db.FacturasVenta.AsNoTracking().Where(filtro)
               join p in db.PedidosFacturables.AsNoTracking() on f.PedidoFacturableId equals p.Id
               where f.Estado != EstadoTimbrado.Cancelado && p.Origen == OrigenPedido.Aw
               select new Factura(f.Id, f.Folio, p.NumeroPedido, true)).ToListAsync(ct);

    private async Task<List<Factura>> Anticipos(
        System.Linq.Expressions.Expression<Func<Domain.Anticipos.FacturaAnticipo, bool>> filtro, CancellationToken ct)
    {
        // Mismo criterio que PedidosSinFacturarReadAdapter: pedido por la factura o por Anticipo.PedidoOrigenRef.
        var r = await db.FacturasAnticipo.AsNoTracking().Where(filtro)
            .Where(f => f.Estado != EstadoTimbrado.Cancelado)
            .Select(f => new
            {
                f.Id,
                f.Folio,
                PorPedido = db.PedidosFacturables.Where(p => p.Id == f.PedidoFacturableId && p.Origen == OrigenPedido.Aw)
                    .Select(p => p.NumeroPedido).FirstOrDefault(),
                PorRef = db.Anticipos.Where(a => a.FacturaAnticipoId == f.Id).Select(a => a.PedidoOrigenRef).FirstOrDefault(),
            })
            .ToListAsync(ct);
        return r.Select(x => new Factura(x.Id, x.Folio, x.PorPedido ?? x.PorRef, false))
            .Where(f => f.Pedido != null).ToList();
    }

    private async Task<List<PagosFacturaAw>> Calcular(IEnumerable<Factura> facturas, CancellationToken ct)
    {
        var lista = facturas.Where(f => f.Pedido != null).ToList();
        if (lista.Count == 0) return [];
        var ids = lista.Select(f => f.Id).ToList();

        var cobros = await db.CobrosMostrador.AsNoTracking()
            .Where(c => ids.Contains(c.ComprobanteId) && c.Estado == EstadoCobroMostrador.Registrado)
            .Select(c => new { c.ComprobanteId, c.Moneda, Formas = c.FormasPago.Select(f => new { f.FormaPago, f.Importe }).ToList() })
            .ToListAsync(ct);
        var repp = await db.RecibosPago.AsNoTracking().Where(r => r.Estado == EstadoTimbrado.Timbrado)
            .SelectMany(r => r.FacturasPagadas.Where(l => ids.Contains(l.FacturaVentaId)))
            .Select(l => new { l.FacturaVentaId, l.FormaPagoReal, l.MonedaFactura, l.ImportePagado })
            .ToListAsync(ct);

        var pagos = cobros.SelectMany(c => c.Formas.Select(f => (Factura: c.ComprobanteId, Forma: f.FormaPago, c.Moneda, f.Importe)))
            .Concat(repp.Select(l => (Factura: l.FacturaVentaId, Forma: l.FormaPagoReal, Moneda: l.MonedaFactura, Importe: l.ImportePagado)))
            .ToList();

        return lista.Select(f => new PagosFacturaAw(f.Pedido!, f.Folio, f.EsFinal,
                pagos.Where(p => p.Factura == f.Id)
                    .GroupBy(p => (p.Forma, p.Moneda))
                    .Select(g => new PagoGrupoAw(g.Key.Forma, g.Key.Moneda, g.Sum(p => p.Importe)))
                    .Where(g => g.Importe != 0)
                    .ToList()))
            .ToList();
    }
}
