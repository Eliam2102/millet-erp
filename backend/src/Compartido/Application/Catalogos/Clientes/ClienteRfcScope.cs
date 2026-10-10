using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>Serializa altas manuales y de A+W por RFC no genérico, sin exigir limpiar duplicados históricos.</summary>
internal sealed class ClienteRfcScope(IDbContextTransaction? transaction) : IAsyncDisposable
{
    public static async Task<ClienteRfcScope> AbrirAsync(CompartidoDbContext db, string? rfc, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || !RfcCliente.EsLigable(rfc)) return new(null);
        var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var normalizado = RfcCliente.Normalizar(rfc);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({normalizado}, 41010))", ct);
            return new(transaction);
        }
        catch { if (transaction is not null) await transaction.DisposeAsync(); throw; }
    }
    public async Task ConfirmarAsync(CancellationToken ct) { if (transaction is not null) await transaction.CommitAsync(ct); }
    public async ValueTask DisposeAsync() { if (transaction is not null) await transaction.DisposeAsync(); }
}
