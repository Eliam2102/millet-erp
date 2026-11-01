using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.CreditoDisponible;

/// <summary>
/// Componentes del crédito disponible por moneda (matriz I7 / R1):
/// <c>disponible = límite − facturado − liberado sin factura</c>. Cálculo
/// ÚNICO para la consulta del disponible, la decisión de liberación y la
/// autorización de pendientes (C7.1, 86e3kdxx4): las tres dan el mismo
/// número. Nunca convierte moneda.
/// </summary>
internal static class ComponentesCredito
{
    /// <summary>Saldo pendiente (total − pagado − NC) de las facturas Abierta/Parcial.</summary>
    public static async Task<Dictionary<string, decimal>> FacturadoPorMonedaAsync(
        CuentasPorCobrarDbContext db, Guid clienteId, CancellationToken cancellationToken) =>
        await db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == clienteId
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial))
            .GroupBy(f => f.Moneda)
            .Select(g => new { Moneda = g.Key, Saldo = g.Sum(f => f.Total - f.MontoPagado - f.MontoNc) })
            .ToDictionaryAsync(x => x.Moneda, x => x.Saldo, cancellationToken);

    /// <summary>
    /// Pedidos a crédito en firme sin factura vigente, menos el anticipo que
    /// los cubre (sin bajar de cero por pedido). Un pedido facturado ya está
    /// en <see cref="FacturadoPorMonedaAsync"/>: no se cuenta dos veces.
    /// <paramref name="pedidoRefExcluido"/> es el pedido que se está
    /// evaluando, que no compromete su propio crédito.
    /// </summary>
    public static async Task<Dictionary<string, decimal>> LiberadoSinFacturaPorMonedaAsync(
        IFacturacionPedidosSinFacturarReadPort pedidos, Guid empresaId, Guid clienteId, string? pedidoRefExcluido,
        CancellationToken cancellationToken)
    {
        var excluido = pedidoRefExcluido?.Trim();
        var lista = await pedidos.ListarPorClienteAsync(empresaId, clienteId, cancellationToken);
        return lista
            .Where(p => excluido is null || !string.Equals(p.NumeroPedido?.Trim(), excluido, StringComparison.Ordinal))
            .GroupBy(p => p.Moneda)
            .ToDictionary(g => g.Key, g => g.Sum(p => Math.Max(0m, p.Total - p.AnticipoLigado)));
    }
}
