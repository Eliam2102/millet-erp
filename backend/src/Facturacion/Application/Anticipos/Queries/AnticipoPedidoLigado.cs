using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Anticipos.Queries;

/// <summary>Pedido de A+W al que quedó ligado un anticipo cobrado antes de que el pedido llegara al ERP.</summary>
public sealed record PedidoLigadoAnticipo(Guid PedidoFacturableId, string NumeroPedido, string Estado);

/// <summary>
/// 86e3tfd6n — ligado calculado (sin columna nueva): un anticipo con número de pedido de A+W queda ligado en cuanto la
/// ingesta importa ese pedido (estatus 15/42 o liberado por el vendedor) para el MISMO cliente. Se liga por número, sin
/// versión: el anticipo sigue al pedido vigente. Un pedido del mismo número pero de otro cliente no liga.
/// </summary>
public static class AnticipoPedidoLigado
{
    public static async Task<IReadOnlyDictionary<(Guid ClienteId, string NumeroPedido), PedidoLigadoAnticipo>> ResolverAsync(
        FacturacionDbContext db, IEnumerable<(Guid ClienteId, string? NumeroPedido)> anticipos, CancellationToken ct)
    {
        var buscados = anticipos.Where(a => a.NumeroPedido is not null).Select(a => (a.ClienteId, NumeroPedido: a.NumeroPedido!)).Distinct().ToList();
        if (buscados.Count == 0) return new Dictionary<(Guid, string), PedidoLigadoAnticipo>();
        var numeros = buscados.Select(b => b.NumeroPedido).Distinct().ToList();
        var clientes = buscados.Select(b => b.ClienteId).Distinct().ToList();
        var pedidos = await db.PedidosFacturables.AsNoTracking()
            .Where(p => p.Origen == OrigenPedido.Aw && p.NumeroPedido != null && numeros.Contains(p.NumeroPedido) && clientes.Contains(p.ClienteId))
            .Select(p => new { p.Id, p.ClienteId, NumeroPedido = p.NumeroPedido!, p.Estado })
            .ToListAsync(ct);
        return pedidos
            .GroupBy(p => (p.ClienteId, p.NumeroPedido))
            .Where(g => buscados.Contains(g.Key))
            .ToDictionary(g => g.Key, g =>
            {
                var p = g.First();
                return new PedidoLigadoAnticipo(p.Id, p.NumeroPedido, p.Estado.ToString());
            });
    }
}
