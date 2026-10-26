using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Catalogo;

/// <summary>
/// Serializa la reserva de folio por (prefijo, año) para que dos registros concurrentes
/// no lean la misma secuencia y generen el mismo folio (M-094). Mismo patrón que
/// <c>DevolucionOrigenLock</c>: advisory lock transaccional, no-op fuera de Npgsql.
/// El lock vive hasta el commit o rollback de la transacción activa; si no había
/// transacción, este helper abre una y el llamador debe confirmarla después de su
/// <c>SaveChangesAsync</c> (la devuelve para ese fin; <c>null</c> si no abrió ninguna).
/// </summary>
internal static class FolioMovimientoLock
{
    public static async Task<IDbContextTransaction?> AbrirAsync(AlmacenDbContext db, string prefijo, int anio, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return null;
        var propia = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var llave = $"folio:{prefijo}:{anio}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", ct);
            return propia;
        }
        catch
        {
            if (propia is not null) await propia.DisposeAsync();
            throw;
        }
    }

    /// <summary>Siguiente folio ENT de la misma secuencia que usan los movimientos (recepciones solo de activos, que no generan movimiento).</summary>
    public static async Task<string> SiguienteEntradaCompraAsync(AlmacenDbContext db, int anio, CancellationToken ct)
    {
        var prefijo = TipoMovimiento.EntradaCompra.PrefijoFolio();
        _ = await AbrirAsync(db, prefijo, anio, ct);
        var secuencia = await db.FolioSecuenciasMovimiento.FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, ct);
        if (secuencia is null)
        {
            secuencia = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            db.FolioSecuenciasMovimiento.Add(secuencia);
        }
        return FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, anio, secuencia.Incrementar()).Valor;
    }
}
