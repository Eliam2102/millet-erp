namespace Millet.ProductoTerminado.Application.Ports;

/// <summary>
/// Saldo por pedido (decisión E4): total menos notas de crédito, anticipos timbrados ligados, cobros de sus facturas y anticipos
/// previos ligados (#208). Lo define PT; canal Real: <c>SaldoPedidoReadAdapter</c> de Facturación (U-3); canal Demo: saldo de prueba.
/// </summary>
public interface ISaldoPedidoReadPort
{
    /// <summary>Saldo del pedido; <c>null</c> = no se conoce, se trata como «no cubierto».</summary>
    Task<decimal?> ObtenerAsync(Guid empresaId, string numeroPedido, CancellationToken ct);
}
