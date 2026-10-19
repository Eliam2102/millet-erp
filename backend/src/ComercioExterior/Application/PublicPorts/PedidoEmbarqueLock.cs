using Microsoft.EntityFrameworkCore;

namespace Millet.ComercioExterior.Application.PublicPorts;

/// <summary>Serializa CE y Facturación solamente para los pedidos afectados.</summary>
public static class PedidoEmbarqueLock
{
    public static async Task ExecuteAsync(DbContext db, IEnumerable<Guid> pedidos,
        Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) { await operation(ct); return; }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Orden estable para operaciones que afectan varios pedidos (cancelación).
        foreach (var id in pedidos.Distinct().Order())
        {
            var clave = "millet:ce:pedido:" + id;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({clave}, 0))", ct);
        }
        await operation(ct);
        await tx.CommitAsync(ct);
    }
}
