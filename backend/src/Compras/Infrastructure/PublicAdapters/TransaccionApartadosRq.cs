using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;

namespace Millet.Compras.Infrastructure.PublicAdapters;

// P7: excepción acotada de atomicidad entre autorización RQ y apartado, mismo PostgreSQL.
public sealed class TransaccionApartadosRq(ComprasDbContext compras, AlmacenDbContext almacen,
    ApartadosRequisicionService apartados)
{
    public ApartadosRequisicionService Apartados => apartados;
    public async Task<IAsyncDisposable> UnirAsync(CancellationToken ct)
    {
        var conexionAnterior = almacen.Database.GetDbConnection();
        await almacen.Database.CloseConnectionAsync();
        almacen.Database.SetDbConnection(compras.Database.GetDbConnection());
        await almacen.Database.UseTransactionAsync(compras.Database.CurrentTransaction!.GetDbTransaction(), ct);
        return new Union(almacen, conexionAnterior);
    }
    private sealed class Union(AlmacenDbContext db, System.Data.Common.DbConnection anterior) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await db.Database.UseTransactionAsync(null, CancellationToken.None);
            db.Database.SetDbConnection(anterior);
        }
    }
}
