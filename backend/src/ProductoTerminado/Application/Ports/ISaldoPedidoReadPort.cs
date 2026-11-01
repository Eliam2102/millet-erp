namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>
/// Saldo por pedido (decisión E4): total menos notas de crédito, anticipos timbrados ligados, cobros de sus facturas y anticipos
/// previos ligados (#208). Lo define PT y lo implementará Facturación (Uziel).
/// PLATFORM-TODO(&lt;SaldoPorPedidoFacturacion&gt;): hoy solo hay adaptadores provisionales (Real y Demo).
/// </summary>
public interface ISaldoPedidoReadPort
{
    /// <summary>Saldo del pedido; <c>null</c> = no se conoce, se trata como «no cubierto».</summary>
    Task<decimal?> ObtenerAsync(Guid empresaId, string numeroPedido, CancellationToken ct);
}
