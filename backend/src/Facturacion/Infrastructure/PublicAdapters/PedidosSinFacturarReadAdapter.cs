using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter público de <see cref="IFacturacionPedidosSinFacturarReadPort"/>
/// (C7.1, 86e3kdxx4). Pedidos a crédito del cliente que entraron en firme y
/// siguen sin CFDI vigente (Importado, Bloqueado o en Excepción de ingesta);
/// los facturados ya están en la cartera y los cancelados no comprometen
/// crédito. El anticipo
/// ligado es el saldo de los anticipos del mismo cliente y moneda cuyo
/// pedido de origen es el pedido (por id del CFDI de anticipo o por folio).
/// <c>Total</c> va con IVA, como el facturado de la cartera y el saldo del
/// anticipo. Lectura <c>AsNoTracking</c> con el filtro de empresa vigente.
/// </summary>
public sealed class PedidosSinFacturarReadAdapter(FacturacionDbContext db) : IFacturacionPedidosSinFacturarReadPort
{
    public async Task<IReadOnlyList<PedidoSinFacturarDto>> ListarPorClienteAsync(
        Guid clienteId, CancellationToken cancellationToken)
    {
        var pedidos = await db.PedidosFacturables.AsNoTracking()
            .Where(p => p.ClienteId == clienteId
                     && p.EsCredito
                     && p.ComprobanteVigenteId == null
                     && p.Estado != EstadoPedidoFacturable.Facturado
                     && p.Estado != EstadoPedidoFacturable.Cancelado)
            .Select(p => new
            {
                p.Id,
                p.NumeroPedido,
                p.Moneda,
                p.Total,
                p.EstadoOrigen,
                Lineas = p.Lineas.Select(l => new { l.Importe, l.TasaIva }).ToList(),
            })
            .ToListAsync(cancellationToken);
        pedidos = pedidos.Where(p => ComprometeCredito(p.EstadoOrigen)).ToList();
        if (pedidos.Count == 0) return [];

        var anticipos = await db.Anticipos.AsNoTracking()
            .Where(a => a.ClienteId == clienteId && a.Estado != EstadoAnticipo.Cancelado && a.Saldo > 0)
            .Select(a => new { a.Moneda, a.Saldo, a.PedidoOrigenRef, a.FacturaAnticipoId })
            .ToListAsync(cancellationToken);
        var facturas = anticipos.Select(a => a.FacturaAnticipoId).ToList();
        var pedidoDeFactura = facturas.Count == 0
            ? []
            : await db.FacturasAnticipo.AsNoTracking()
                .Where(f => facturas.Contains(f.Id) && f.PedidoFacturableId != null)
                .ToDictionaryAsync(f => f.Id, f => f.PedidoFacturableId!.Value, cancellationToken);

        return pedidos.Select(p =>
        {
            var ligado = anticipos
                .Where(a => a.Moneda == p.Moneda
                         && ((pedidoDeFactura.TryGetValue(a.FacturaAnticipoId, out var pedidoId) && pedidoId == p.Id)
                          || (p.NumeroPedido != null && a.PedidoOrigenRef == p.NumeroPedido)))
                .Sum(a => a.Saldo);
            // Mismo total con IVA que la bandeja (86e3tfd7j): IVA redondeado por
            // línea como la factura. Pedido legacy con alguna línea sin tasa:
            // subtotal (no se inventa la tasa).
            var total = p.Lineas.Any(l => l.TasaIva is null)
                ? p.Total
                : p.Total + p.Lineas.Sum(l => Math.Round(l.Importe * l.TasaIva!.Value, 2, MidpointRounding.AwayFromZero));
            return new PedidoSinFacturarDto(p.Id, p.NumeroPedido, p.Moneda, total, ligado);
        }).ToList();
    }

    /// <summary>
    /// V22 (propuesta P7.1, a validar con Crédito): el pedido compromete
    /// crédito desde el estatus A+W 40 (transmitido a producción; incluye 42
    /// máquina), o desde 15 (taller/almacenable) en la parte que su anticipo
    /// no cubre — el anticipo se descuenta en ambos casos. 900/999 son
    /// cancelación. Un estatus no numérico o vacío (orígenes sin catálogo
    /// A+W) compromete mientras siga sin factura.
    /// </summary>
    internal static bool ComprometeCredito(string? estadoOrigen) =>
        !int.TryParse(estadoOrigen?.Trim(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var estatus)
        || estatus == 15
        || (estatus >= 40 && estatus < 900);
}
