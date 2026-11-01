using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Infrastructure.Persistence;

namespace Millet.ProductoTerminado.Infrastructure.PublicAdapters;

/// <summary>
/// Canal Real. PLATFORM-TODO(&lt;SaldoPorPedidoFacturacion&gt;): siempre «no cubierto» hasta que Facturación (Uziel) publique el puerto;
/// nunca libera por saldo, solo REPARTO y la decisión de CxC.
/// </summary>
public sealed class SaldoPedidoProvisionalAdapter : ISaldoPedidoReadPort
{
    public Task<decimal?> ObtenerAsync(Guid empresaId, string numeroPedido, CancellationToken ct) => Task.FromResult<decimal?>(null);
}

/// <summary>
/// Canal Demo: saldo de prueba por pedido (<c>saldos_demo</c>); sin fila arranca en el total del pedido.
/// TODO(Uziel): las notas de crédito (reducen el total del pedido) y los cobros de sus facturas no existen en PT; se resuelven en el puerto real.
/// </summary>
public sealed class SaldoPedidoDemoAdapter(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidos) : ISaldoPedidoReadPort
{
    public async Task<decimal?> ObtenerAsync(Guid empresaId, string numeroPedido, CancellationToken ct)
    {
        var numero = numeroPedido.Trim();
        var fila = await db.SaldosDemo.AsNoTracking().FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.NumeroPedido == numero, ct);
        if (fila is not null) return fila.Saldo;
        return (await pedidos.ObtenerPorNumeroAsync(empresaId, numero, ct))?.Total;
    }
}
