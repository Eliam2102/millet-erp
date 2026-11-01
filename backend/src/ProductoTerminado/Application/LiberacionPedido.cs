using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Application;

/// <summary>
/// Regla 69 → 70 del pedido (§6 del diseño). Muta el pedido y publica al outbox, pero nunca llama a <c>SaveChanges</c>.
/// Cada cambio de estado ocurre una sola vez: <see cref="PedidoPt.Retener"/> y <see cref="PedidoPt.Liberar"/> devuelven false si ya aplicaron.
/// </summary>
public sealed class LiberacionPedido(ISaldoPedidoReadPort saldos, IIntegrationEventPublisher eventos, IClock clock)
{
    /// <summary>Primera pieza: entra a 69; si saldo o REPARTO liberan, pasa directo a 70 (sin evento de 69); si no, publica Retenido para que CxC decida.</summary>
    public async Task EntrarA69Async(PedidoPt pedido, PedidoPtOrigen origen, CancellationToken ct)
    {
        var motivo = await MotivoAutomaticoAsync(pedido, ct);
        if (motivo is not null) { await LiberarAsync(pedido, motivo, ct); return; }
        if (pedido.Retener(clock.UtcNow))
            await eventos.PublishAsync(new PedidoEstadoCambiadoEvent(pedido.EmpresaId, clock.UtcNow, pedido.Id, pedido.NumeroPedido,
                nameof(EstadoLiberacion.Retenido), MotivosPt.SaldoPendiente, origen.ClienteId, origen.Moneda, origen.Total), ct);
    }

    /// <summary>Reevalúa un pedido retenido por saldo o REPARTO. Devuelve true si lo liberó.</summary>
    public async Task<bool> ReevaluarAsync(PedidoPt pedido, CancellationToken ct)
    {
        if (pedido.Liberacion != EstadoLiberacion.Retenido) return false;
        var motivo = await MotivoAutomaticoAsync(pedido, ct);
        return motivo is not null && await LiberarAsync(pedido, motivo, ct);
    }

    public async Task<bool> LiberarAsync(PedidoPt pedido, string motivo, CancellationToken ct)
    {
        var ahora = clock.UtcNow;
        if (!pedido.Liberar(motivo, ahora)) return false;
        await eventos.PublishAsync(new PedidoEstadoCambiadoEvent(pedido.EmpresaId, ahora, pedido.Id, pedido.NumeroPedido,
            nameof(EstadoLiberacion.Liberado), motivo, null, null, null), ct);
        return true;
    }

    // Saldo primero (E4); REPARTO libera sin verificar pago ni completitud (Q2).
    private async Task<string?> MotivoAutomaticoAsync(PedidoPt pedido, CancellationToken ct)
    {
        if (await saldos.ObtenerAsync(pedido.EmpresaId, pedido.NumeroPedido, ct) is <= 0m) return MotivosPt.SaldoCubierto;
        return string.Equals(pedido.CondicionPagoOrigen?.Trim(), "REPARTO", StringComparison.OrdinalIgnoreCase) ? MotivosPt.Reparto : null;
    }
}
