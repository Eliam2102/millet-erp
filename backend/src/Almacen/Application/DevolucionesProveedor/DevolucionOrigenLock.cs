using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.DevolucionesProveedor;

/// <summary>Serializa devoluciones del mismo origen para que dos capturas no consuman el mismo saldo.</summary>
internal static class DevolucionOrigenLock
{
    public static async Task<IDbContextTransaction?> AbrirAsync(AlmacenDbContext db, Guid? origenId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || origenId is null) return null;
        var propia = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({origenId.Value.ToString()}, 0))", ct);
            return propia;
        }
        catch
        {
            if (propia is not null) await propia.DisposeAsync();
            throw;
        }
    }
}
