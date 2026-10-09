using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Npgsql;

namespace Millet.Compras.Infrastructure.PublicAdapters;

// P7: excepción acotada de atomicidad entre autorización RQ y apartado, mismo PostgreSQL.
public sealed class TransaccionApartadosRq(ComprasDbContext compras, AlmacenDbContext almacen,
    ApartadosRequisicionService apartados)
{
    public ApartadosRequisicionService Apartados => apartados;
    public async Task<IAsyncDisposable> UnirAsync(CancellationToken ct)
    {
        var transaccion = compras.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Se requiere una transacción de Compras para apartar existencia.");
        var conexionAnterior = almacen.Database.GetDbConnection();
        await almacen.Database.CloseConnectionAsync();
        var opciones = RelationalOptionsExtension.Extract(almacen.GetService<IDbContextOptions>());
        var propia = opciones.Connection is null || opciones.IsConnectionOwned;
        // SetDbConnection libera la conexión anterior cuando EF es su dueño.
        // Clone conserva la configuración de Npgsql (incluida autenticación),
        // sin volver a instalar el objeto ya liberado. Una conexión externa
        // prestada conserva su identidad y continúa a cargo de su llamador.
        var restaurada = propia
            ? (NpgsqlConnection)((ICloneable)conexionAnterior).Clone()
            : conexionAnterior;
        var union = new Union(almacen, restaurada, propia);
        try
        {
            almacen.Database.SetDbConnection(compras.Database.GetDbConnection(), contextOwnsConnection: false);
            await almacen.Database.UseTransactionAsync(transaccion, ct);
            return union;
        }
        catch
        {
            await union.DisposeAsync();
            throw;
        }
    }
    private sealed class Union(AlmacenDbContext db, System.Data.Common.DbConnection restaurada, bool propia) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await db.Database.UseTransactionAsync(null, CancellationToken.None);
            // Compras sigue siendo dueño de su conexión y de la transacción.
            db.Database.SetDbConnection(restaurada, contextOwnsConnection: propia);
        }
    }
}
