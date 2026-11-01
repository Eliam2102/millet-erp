using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Application;

/// <summary>
/// Regla 69 → 70 del pedido (§6 del diseño). El pedido solo entra a 69 con todas sus piezas esperadas en PT (P-096, SRC-112/114). Muta el pedido y publica al outbox, pero nunca llama a <c>SaveChanges</c>.
/// Cada cambio de estado ocurre una sola vez: <see cref="PedidoPt.Retener"/> y <see cref="PedidoPt.Liberar"/> devuelven false si ya aplicaron.
/// </summary>
public sealed class LiberacionPedido(ProductoTerminadoDbContext db, ISaldoPedidoReadPort saldos, IIntegrationEventPublisher eventos, IClock clock)
{
    /// <summary>Con el pedido completo (100 % de piezas en PT) entra a 69; si saldo o REPARTO liberan, pasa directo a 70 (sin evento de 69); si no, publica Retenido para que CxC decida. Incompleto: no hace nada.</summary>
    public async Task EntrarA69Async(PedidoPt pedido, PedidoPtOrigen origen, CancellationToken ct)
    {
        if (!await CompletoAsync(pedido, ct)) return;
        var motivo = await MotivoAutomaticoAsync(pedido, ct);
        if (motivo is not null) { await LiberarAsync(pedido, motivo, ct); return; }
        if (pedido.Retener(clock.UtcNow))
            await eventos.PublishAsync(new PedidoEstadoCambiadoEvent(pedido.EmpresaId, clock.UtcNow, pedido.Id, pedido.NumeroPedido,
                nameof(EstadoLiberacion.Retenido), MotivosPt.SaldoPendiente, origen.ClienteId, origen.Moneda, origen.Total), ct);
    }

    /// <summary>Reevalúa un pedido retenido (completo) por saldo o REPARTO. Devuelve true si lo liberó.</summary>
    public async Task<bool> ReevaluarAsync(PedidoPt pedido, CancellationToken ct)
    {
        if (pedido.Liberacion != EstadoLiberacion.Retenido || !await CompletoAsync(pedido, ct)) return false;
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

    /// <summary>Todas las piezas esperadas (suma de posiciones, que deben estar cargadas) ya están en PT, incluidas las aún sin guardar. Sin piezas esperadas no hay pedido completo.</summary>
    private async Task<bool> CompletoAsync(PedidoPt pedido, CancellationToken ct)
    {
        var esperadas = pedido.Posiciones.Sum(p => p.PiezasEsperadas);
        if (esperadas <= 0) return false;
        var locales = db.Piezas.Local.Where(p => p.PedidoPtId == pedido.Id).Select(p => p.Id).ToList();
        var guardadas = await db.Piezas.CountAsync(p => p.PedidoPtId == pedido.Id && !locales.Contains(p.Id), ct);
        return locales.Count + guardadas >= esperadas;
    }

    // Saldo primero (E4); REPARTO libera sin verificar pago, pero también con el pedido completo (P-096, SRC-112/114); si debe salir incompleto se cambia aquí.
    private async Task<string?> MotivoAutomaticoAsync(PedidoPt pedido, CancellationToken ct)
    {
        if (await saldos.ObtenerAsync(pedido.EmpresaId, pedido.NumeroPedido, ct) is <= 0m) return MotivosPt.SaldoCubierto;
        return string.Equals(pedido.CondicionPagoOrigen?.Trim(), "REPARTO", StringComparison.OrdinalIgnoreCase) ? MotivosPt.Reparto : null;
    }
}
