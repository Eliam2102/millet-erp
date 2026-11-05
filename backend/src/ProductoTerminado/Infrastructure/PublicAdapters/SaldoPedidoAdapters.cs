using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Infrastructure.Persistence;

namespace Millet.ProductoTerminado.Infrastructure.PublicAdapters;

/// <summary>
/// Canal Demo: saldo de prueba por pedido (<c>saldos_demo</c>); sin fila arranca en el total del pedido.
/// Las notas de crédito y los cobros de sus facturas solo los ve el canal Real (<c>SaldoPedidoReadAdapter</c> de Facturación).
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
