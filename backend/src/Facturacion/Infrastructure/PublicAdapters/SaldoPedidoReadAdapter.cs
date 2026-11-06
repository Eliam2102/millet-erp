using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Saldo real por pedido para Producto Terminado (decisión E4, U-3 · 86e3yxp2b). Canal Real; el Demo sigue con su saldo de prueba.
/// <list type="bullet">
///   <item>Facturas vigentes del pedido (timbradas o con cancelación pendiente): las del pedido, las de sus salidas parciales y su
///   comprobante vigente. De cada una cuenta su saldo en la cartera de CxC, que ya descuenta cobros (REPP y mostrador) y notas de
///   crédito, incluidas las de amortización de anticipos. Si la cartera aún no la proyecta, cuenta su total.</item>
///   <item>Lo que falta facturar: total del pedido con IVA menos lo facturado (cero si el pedido ya está <c>Facturado</c>).</item>
///   <item>Menos los anticipos abiertos sin aplicar: los ligados al pedido (por su factura de anticipo o por folio) y los vinculados
///   a una de sus facturas sin NC de amortización todavía.</item>
/// </list>
/// Un saldo de hasta <see cref="ToleranciaRedondeo"/> se considera pagado (propuesta a Millet, a validar). <c>null</c> = pedido
/// no ingerido o cancelado: PT lo trata como «no cubierto».
/// </summary>
public sealed class SaldoPedidoReadAdapter(FacturacionDbContext db, ISaldoFacturaReadPort cartera, ICurrentEmpresaContext empresa)
    : ISaldoPedidoReadPort
{
    /// <summary>Tolerancia por redondeo para dar el pedido por pagado (propuesta U-3, a validar con Crédito y Cobranza).</summary>
    public const decimal ToleranciaRedondeo = 1.00m;

    private static readonly EstadoTimbrado[] Vigentes = [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente];

    public async Task<decimal?> ObtenerAsync(Guid empresaId, string numeroPedido, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        var numero = numeroPedido.Trim();
        var pedido = await db.PedidosFacturables.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.Origen == OrigenPedido.Aw && p.NumeroPedido == numero)
            .Select(p => new
            {
                p.Id,
                p.Moneda,
                p.Total,
                p.Estado,
                p.ComprobanteVigenteId,
                Lineas = p.Lineas.Select(l => new { l.Importe, l.TasaIva }).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (pedido is null || pedido.Estado == EstadoPedidoFacturable.Cancelado) return null;

        var salidas = db.SalidasFacturables.Where(s => s.PedidoFacturableId == pedido.Id).Select(s => s.Id);
        var facturas = await db.FacturasVenta.AsNoTracking()
            .Where(f => f.EmpresaId == empresaId && Vigentes.Contains(f.Estado)
                && (f.PedidoFacturableId == pedido.Id
                    || (f.SalidaFacturableId != null && salidas.Contains(f.SalidaFacturableId.Value))
                    || f.Id == pedido.ComprobanteVigenteId))
            .Select(f => new { f.Id, f.Total })
            .ToListAsync(ct);

        var porCobrar = 0m;
        foreach (var f in facturas)
            porCobrar += await cartera.ObtenerSaldoAsync(f.Id, ct) ?? f.Total;

        var sinFacturar = pedido.Estado == EstadoPedidoFacturable.Facturado
            ? 0m
            : Math.Max(0m, TotalConIva(pedido.Total, pedido.Lineas.Select(l => (l.Importe, l.TasaIva))) - facturas.Sum(f => f.Total));

        var saldo = porCobrar + sinFacturar - await AnticiposSinAplicarAsync(empresaId, pedido.Id, numero, pedido.Moneda,
            facturas.Select(f => f.Id).ToList(), ct);
        return saldo > 0m && saldo <= ToleranciaRedondeo ? 0m : saldo;
    }

    /// <summary>Mismo total con IVA que la bandeja y los pedidos sin facturar (86e3tfd7j): IVA redondeado por línea; con alguna línea sin tasa, el subtotal.</summary>
    internal static decimal TotalConIva(decimal total, IEnumerable<(decimal Importe, decimal? TasaIva)> lineas)
    {
        var l = lineas.ToList();
        return l.Any(x => x.TasaIva is null)
            ? total
            : total + l.Sum(x => Math.Round(x.Importe * x.TasaIva!.Value, 2, MidpointRounding.AwayFromZero));
    }

    private async Task<decimal> AnticiposSinAplicarAsync(Guid empresaId, Guid pedidoId, string numero, string moneda,
        IReadOnlyCollection<Guid> facturas, CancellationToken ct)
    {
        var anticipos = await db.Anticipos.AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.Estado == EstadoAnticipo.Abierto && a.Saldo > 0 && a.Moneda == moneda
                && (a.PedidoOrigenRef == numero
                    || db.FacturasAnticipo.Any(fa => fa.Id == a.FacturaAnticipoId && fa.PedidoFacturableId == pedidoId)
                    || a.Vinculaciones.Any(v => v.NcAmortizacionId == null && facturas.Contains(v.FacturaVentaId))))
            .Select(a => new
            {
                a.Saldo,
                Ligado = a.PedidoOrigenRef == numero
                    || db.FacturasAnticipo.Any(fa => fa.Id == a.FacturaAnticipoId && fa.PedidoFacturableId == pedidoId),
                Vinculado = a.Vinculaciones.Where(v => v.NcAmortizacionId == null && facturas.Contains(v.FacturaVentaId)).Sum(v => v.Importe),
            })
            .ToListAsync(ct);
        return anticipos.Sum(a => a.Ligado ? a.Saldo : Math.Min(a.Saldo, a.Vinculado));
    }
}
