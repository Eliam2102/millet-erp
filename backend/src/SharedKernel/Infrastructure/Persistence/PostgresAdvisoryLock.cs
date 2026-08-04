using Microsoft.EntityFrameworkCore;

namespace Millet.SharedKernel.Infrastructure.Persistence;

/// <summary>
/// Serializa operaciones cortas entre instancias que comparten PostgreSQL.
/// El bloqueo es transaccional, por lo que se libera también si la operación
/// falla o es cancelada.
/// </summary>
public static class PostgresAdvisoryLock
{
    public static async Task ExecuteAsync(
        DbContext db,
        long lockId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})",
            cancellationToken);

        await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
