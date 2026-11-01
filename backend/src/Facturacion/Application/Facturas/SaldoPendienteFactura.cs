using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Facturas;

/// <summary>
/// F1-CXC-05 Parte 2: saldo pendiente de una factura de venta para no acreditarle más de lo que debe (sin saldos negativos).
/// Manda el saldo de CxC (pagos y NC); si CxC aún no la tiene en cartera, se descuentan las NC timbradas que Facturación
/// conoce.
/// </summary>
internal static class SaldoPendienteFactura
{
    public static async Task<decimal> CalcularAsync(
        FacturacionDbContext db, ISaldoFacturaReadPort saldos, Guid facturaId, decimal total, CancellationToken ct)
    {
        if (await saldos.ObtenerSaldoAsync(facturaId, ct) is decimal saldo) return saldo;
        var acreditado = await db.NotasCredito.AsNoTracking()
            .Where(n => n.FacturaRelacionadaId == facturaId && n.Estado == EstadoTimbrado.Timbrado)
            .SumAsync(n => (decimal?)n.Total, ct) ?? 0m;
        return total - acreditado;
    }
}
