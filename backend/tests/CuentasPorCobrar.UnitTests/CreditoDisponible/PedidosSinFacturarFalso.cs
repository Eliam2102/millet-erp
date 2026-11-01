using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

namespace Millet.CuentasPorCobrar.UnitTests.CreditoDisponible;

/// <summary>Doble del puerto de Facturación para C7.1: pedidos sin facturar por cliente.</summary>
public sealed class PedidosSinFacturarFalso : IFacturacionPedidosSinFacturarReadPort
{
    private readonly List<(Guid ClienteId, PedidoSinFacturarDto Pedido)> _pedidos = [];

    public PedidosSinFacturarFalso Agregar(
        Guid clienteId, string numeroPedido, decimal total, decimal anticipoLigado = 0m, string moneda = "MXN")
    {
        _pedidos.Add((clienteId, new PedidoSinFacturarDto(Guid.NewGuid(), numeroPedido, moneda, total, anticipoLigado)));
        return this;
    }

    public Task<IReadOnlyList<PedidoSinFacturarDto>> ListarPorClienteAsync(
        Guid clienteId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PedidoSinFacturarDto>>(
            _pedidos.Where(p => p.ClienteId == clienteId).Select(p => p.Pedido).ToList());
}
