using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Facturas;

/// <summary>
/// Cálculo canónico del monto acreditado a una factura por notas de crédito
/// timbradas — amortización de anticipos (relación 07) y NC generales
/// (bonificación/devolución). <c>[Decisión 13-K]</c>: las NC siempre acreditan
/// al saldo por cobrar (nunca se reembolsan en efectivo), por lo que todo
/// punto que calcule un "monto a cobrar" debe restarlas: REPP (saldo del
/// complemento de pago), cobro de mostrador, bandeja de cobrables y detalle
/// de factura.
/// </summary>
public static class SaldoPorCobrar
{
    /// <summary>
    /// Total acreditado por NC timbradas, por factura. Las NC en cualquier
    /// otro estado (borrador, fallida, cancelada, descartada) no acreditan.
    /// Facturas sin NC no aparecen en el diccionario.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, decimal>> AcreditadoPorFacturaAsync(
        FacturacionDbContext db, IReadOnlyCollection<Guid> facturaIds, CancellationToken cancellationToken)
    {
        if (facturaIds.Count == 0)
            return new Dictionary<Guid, decimal>();

        return await db.NotasCredito.AsNoTracking()
            .Where(n => n.FacturaRelacionadaId != null
                        && facturaIds.Contains(n.FacturaRelacionadaId.Value)
                        && n.Estado == EstadoTimbrado.Timbrado)
            .GroupBy(n => n.FacturaRelacionadaId!.Value)
            .Select(g => new { FacturaId = g.Key, Total = g.Sum(n => n.Total) })
            .ToDictionaryAsync(x => x.FacturaId, x => x.Total, cancellationToken);
    }

    /// <summary>Total acreditado por NC timbradas de una sola factura.</summary>
    public static async Task<decimal> AcreditadoAsync(
        FacturacionDbContext db, Guid facturaId, CancellationToken cancellationToken)
        => (await AcreditadoPorFacturaAsync(db, [facturaId], cancellationToken)).GetValueOrDefault(facturaId);

    /// <summary>
    /// Pagos REPP vigentes: excluye complementos <c>Cancelado</c>/<c>Descartada</c>,
    /// que no descuentan saldo ni consumen parcialidad (un fallido pendiente de
    /// reintento sí cuenta — se resolverá sobre el mismo folio).
    /// </summary>
    public static IQueryable<Domain.Repp.ReciboPagoFactura> PagosVigentes(FacturacionDbContext db)
        => db.Set<Domain.Repp.ReciboPagoFactura>()
            .Join(
                db.RecibosPago.Where(r =>
                    r.Estado != EstadoTimbrado.Cancelado && r.Estado != EstadoTimbrado.Descartada),
                p => p.ReciboPagoId,
                r => r.Id,
                (p, _) => p);
}
