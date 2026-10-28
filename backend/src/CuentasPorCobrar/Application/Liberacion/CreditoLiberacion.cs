using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

/// <summary>Foto del crédito al decidir: ¿hay línea Activa? y su disponible.</summary>
internal readonly record struct CreditoLiberacionSnapshot(bool LineaActiva, decimal Disponible);

/// <summary>
/// Cálculo ÚNICO del crédito disponible para la liberación de pedidos.
/// Lo usan «Decidir» (cascada) y «Autorizar» una pendiente (C7.3: al
/// autorizar se recalcula con el mismo criterio, por si entró un pago o se
/// bloqueó la línea mientras esperaba).
/// </summary>
internal static class CreditoLiberacion
{
    public static async Task<CreditoLiberacionSnapshot> CalcularAsync(
        CuentasPorCobrarDbContext db, Guid clienteId, string moneda, CancellationToken cancellationToken)
    {
        // Línea Bloqueada/Suspendida = sin línea activa (supuesto C7.3 e).
        var linea = await db.LineasCredito.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ClienteId == clienteId
                                   && l.Moneda == moneda
                                   && l.Estado == EstadoLineaCredito.Activa,
                cancellationToken);
        if (linea is null) return new CreditoLiberacionSnapshot(false, 0m);

        // Mismo cálculo que CreditoDisponibleQuery: saldo pendiente de las
        // facturas Abierta/Parcial. `liberado_sin_factura` sigue en 0
        // (PLATFORM-TODO(<CreditoLiberadoSinFactura>), gap G1).
        var facturado = await db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == clienteId
                     && f.Moneda == moneda
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial))
            .SumAsync(f => f.Total - f.MontoPagado - f.MontoNc, cancellationToken);

        return new CreditoLiberacionSnapshot(true, linea.Limite - facturado);
    }
}
