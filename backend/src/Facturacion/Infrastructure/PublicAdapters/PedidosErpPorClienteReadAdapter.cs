using Microsoft.EntityFrameworkCore;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>Cuenta pedidos facturables por cliente (solo lectura) para decidir Fusionar o Agrupar al unificar por RFC.</summary>
/// <remarks>El cliente es maestro global: cuenta todas las empresas (y funciona sin contexto de empresa, como en la unificación automática).</remarks>
public sealed class PedidosErpPorClienteReadAdapter(FacturacionDbContext db, ICurrentEmpresaContext empresa) : IPedidosErpPorClienteReadPort
{
    public async Task<IReadOnlyDictionary<Guid, int>> ContarAsync(IReadOnlyCollection<Guid> clienteIds, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return clienteIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.PedidosFacturables.AsNoTracking().Where(p => clienteIds.Contains(p.ClienteId))
                .GroupBy(p => p.ClienteId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }
}
