using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.CreditoDisponible;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

/// <summary>Foto del crédito al decidir: ¿hay línea Activa? y su disponible.</summary>
internal readonly record struct CreditoLiberacionSnapshot(bool LineaActiva, decimal Disponible);

/// <summary>
/// Crédito disponible para la liberación de pedidos. Lo usan «Decidir»
/// (cascada) y «Autorizar» una pendiente (C7.3: al autorizar se recalcula
/// con el mismo criterio, por si entró un pago o se bloqueó la línea
/// mientras esperaba). Usa los mismos componentes que la consulta del
/// disponible (C7.1, <see cref="ComponentesCredito"/>).
/// </summary>
internal static class CreditoLiberacion
{
    public static async Task<CreditoLiberacionSnapshot> CalcularAsync(
        CuentasPorCobrarDbContext db, IFacturacionPedidosSinFacturarReadPort pedidos,
        Guid clienteId, string moneda, string pedidoRef, CancellationToken cancellationToken)
    {
        // Línea Bloqueada/Suspendida = sin línea activa (supuesto C7.3 e).
        var linea = await db.LineasCredito.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ClienteId == clienteId
                                   && l.Moneda == moneda
                                   && l.Estado == EstadoLineaCredito.Activa,
                cancellationToken);
        if (linea is null) return new CreditoLiberacionSnapshot(false, 0m);

        var facturado = (await ComponentesCredito.FacturadoPorMonedaAsync(db, clienteId, cancellationToken))
            .GetValueOrDefault(moneda, 0m);
        var liberadoSinFactura = (await ComponentesCredito.LiberadoSinFacturaPorMonedaAsync(
                pedidos, clienteId, pedidoRef, cancellationToken))
            .GetValueOrDefault(moneda, 0m);

        return new CreditoLiberacionSnapshot(true, linea.Limite - facturado - liberadoSinFactura);
    }
}
