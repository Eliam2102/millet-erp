using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Persistence;
namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;
/// <summary>Unidad de trabajo local: el puerto contable y el activo confirman o revierten juntos en la misma base PostgreSQL.</summary>
public sealed class TransaccionPolizaAdapter(ActivosFijosDbContext activos, ContabilidadDbContext contabilidad) : ITransaccionPolizaActivo
{
    public async Task<IAsyncDisposable> VincularAsync(CancellationToken ct)
    {
        var tx = activos.Database.CurrentTransaction ?? throw new InvalidOperationException("La mutación del activo requiere transacción.");
        contabilidad.Database.SetDbConnection(activos.Database.GetDbConnection(), contextOwnsConnection: false);
        return await contabilidad.Database.UseTransactionAsync(tx.GetDbTransaction(), ct)
            ?? throw new InvalidOperationException("No se pudo compartir la transacción contable.");
    }
}
