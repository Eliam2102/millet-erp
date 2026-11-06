using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>Implementa <see cref="IPendientesMaestroReadPort"/> con los pedidos del cliente que siguen por facturar (Importado o Bloqueado).</summary>
public sealed class PedidosPendientesClienteReadAdapter(FacturacionDbContext db, ICurrentEmpresaContext empresa)
    : IPendientesMaestroReadPort
{
    public async Task<IReadOnlyList<string>> ObtenerAsync(TipoMaestro tipo, Guid id, CancellationToken cancellationToken)
    {
        if (tipo != TipoMaestro.Cliente) return [];
        using var bypass = empresa.Bypass();
        var pedidos = await db.PedidosFacturables.AsNoTracking()
            .Where(p => p.ClienteId == id
                        && (p.Estado == EstadoPedidoFacturable.Importado || p.Estado == EstadoPedidoFacturable.Bloqueado))
            .OrderBy(p => p.NumeroPedido)
            .Select(p => new { p.NumeroPedido, p.Total, p.Moneda }).ToListAsync(cancellationToken);
        return pedidos.Select(p => $"Pedido {p.NumeroPedido ?? "sin número"} por facturar ({p.Total:N2} {p.Moneda}).").ToList();
    }
}
