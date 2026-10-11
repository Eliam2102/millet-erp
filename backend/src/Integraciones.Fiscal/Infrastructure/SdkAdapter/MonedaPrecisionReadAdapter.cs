using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;
public sealed class MonedaPrecisionReadAdapter(CompartidoDbContext db) : IMonedaPrecisionReadPort
{
    public async Task<IReadOnlyDictionary<string, int>> ObtenerAsync(IReadOnlyCollection<string> monedas, CancellationToken ct) =>
        await db.Monedas.AsNoTracking().Where(m => monedas.Contains(m.Codigo))
            .ToDictionaryAsync(m => m.Codigo, m => m.Decimales, ct);
}
