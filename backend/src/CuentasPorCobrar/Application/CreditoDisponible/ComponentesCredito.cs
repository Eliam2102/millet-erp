using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Liberacion;
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
    /// <para>M2 (prueba S3-4, supuesto a validar con Crédito): un pedido que CxC liberó (su última decisión es
    /// Liberado o LiberadoConOverride) resta crédito desde ese momento, aunque A+W aún no lo pase a 40. Cada pedido
    /// se cuenta una sola vez: por estatus o por liberación, no por ambos. Sin esto, dos liberaciones antes de que A+W
    /// transmita veían el mismo disponible.</para>
    /// </summary>
    public static async Task<Dictionary<string, decimal>> LiberadoSinFacturaPorMonedaAsync(
        CuentasPorCobrarDbContext db, IFacturacionPedidosSinFacturarReadPort pedidos, Guid empresaId, Guid clienteId,
        string? pedidoRefExcluido, CancellationToken cancellationToken)
    {
        var excluido = pedidoRefExcluido?.Trim();
        var lista = await pedidos.ListarPorClienteAsync(empresaId, clienteId, cancellationToken);
        var liberados = lista.Any(p => !p.ComprometeCredito)
            ? await PedidosLiberadosAsync(db, empresaId, clienteId, cancellationToken)
            : [];
        return lista
            .Where(p => excluido is null || !string.Equals(p.NumeroPedido?.Trim(), excluido, StringComparison.Ordinal))
            .Where(p => p.ComprometeCredito || (p.NumeroPedido is not null && liberados.Contains(p.NumeroPedido.Trim())))
            .GroupBy(p => p.Moneda)
            .ToDictionary(g => g.Key, g => g.Sum(p => Math.Max(0m, p.Total - p.AnticipoLigado)));
    }

    /// <summary>Folios del cliente cuya última decisión de liberación los liberó (con crédito, serie u override).</summary>
    private static async Task<HashSet<string>> PedidosLiberadosAsync(
        CuentasPorCobrarDbContext db, Guid empresaId, Guid clienteId, CancellationToken cancellationToken)
    {
        var decisiones = await db.DecisionesLiberacion.AsNoTracking()
            .Where(d => d.EmpresaId == empresaId && d.ClienteId == clienteId)
            .Select(d => new { d.PedidoRef, d.Resultado, d.DecididoEn })
            .ToListAsync(cancellationToken);
        return decisiones
            .GroupBy(d => d.PedidoRef.Trim())
            .Where(g => g.MaxBy(d => d.DecididoEn)!.Resultado
                is ResultadoLiberacion.Liberado or ResultadoLiberacion.LiberadoConOverride)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
    }
}
