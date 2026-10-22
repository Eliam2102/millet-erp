using Microsoft.EntityFrameworkCore;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>Cuenta pedidos facturables por cliente (solo lectura) para decidir Fusionar o Agrupar al unificar por RFC.</summary>
public sealed class PedidosErpPorClienteReadAdapter(FacturacionDbContext db) : IPedidosErpPorClienteReadPort
{
    public async Task<IReadOnlyDictionary<Guid, int>> ContarAsync(IReadOnlyCollection<Guid> clienteIds, CancellationToken ct) =>
        clienteIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.PedidosFacturables.AsNoTracking().Where(p => clienteIds.Contains(p.ClienteId))
                .GroupBy(p => p.ClienteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
}
