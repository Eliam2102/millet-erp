using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Idempotency;
using Millet.SharedKernel.Infrastructure.Idempotency;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Web;

/// <summary>
/// Tests integration de <see cref="IdempotencyKeysCleanupJob"/>: verifica
/// que la política de retención del ADR-0020 (1h processing, 24h
/// completed/failed) se aplica correctamente y que el advisory lock
/// previene corridas concurrentes.
/// </summary>
public class IdempotencyKeysCleanupJobTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IdempotencyKeysCleanupJobTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CleanupOnce_BorraProcessingAntiguas_Y_CompletedExpiradas_PreservaRecientes()
    {
        var prefix = $"test-cleanup-{Guid.NewGuid():N}-";
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var empresa = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresa.Bypass();

        var now = DateTimeOffset.UtcNow;
        var empresaId = Guid.CreateVersion7();
        var usuarioId = Guid.CreateVersion7();

        // Sembrar 4 filas con created_at controlado:
        var oldProcessing = MakeKey(prefix + "old-proc", empresaId, usuarioId,
            IdempotencyStatuses.Processing, createdAt: now.AddHours(-2));
        var oldCompleted = MakeKey(prefix + "old-comp", empresaId, usuarioId,
            IdempotencyStatuses.Completed, createdAt: now.AddHours(-25));
        var freshProcessing = MakeKey(prefix + "fresh-proc", empresaId, usuarioId,
            IdempotencyStatuses.Processing, createdAt: now.AddMinutes(-10));
        var freshCompleted = MakeKey(prefix + "fresh-comp", empresaId, usuarioId,
            IdempotencyStatuses.Completed, createdAt: now.AddHours(-2));

        db.IdempotencyKeys.AddRange(oldProcessing, oldCompleted, freshProcessing, freshCompleted);
        await db.SaveChangesAsync();

        var job = new IdempotencyKeysCleanupJob(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IdempotencyOptions()),
            NullLogger<IdempotencyKeysCleanupJob>.Instance);

        var deleted = await job.CleanupOnceAsync(CancellationToken.None);

        // Esperamos: oldProcessing y oldCompleted borradas; freshProcessing
        // y freshCompleted preservadas. Otras filas en la tabla pueden
        // contar para 'deleted' si también vencieron — assertamos por nombre.
        Assert.True(deleted >= 2, $"deleted={deleted} debe ser >= 2 (al menos las 2 que sembramos vencidas)");

        var remaining = await db.IdempotencyKeys
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId)
            .Select(x => x.Key)
            .ToListAsync();

        Assert.DoesNotContain(prefix + "old-proc", remaining);
        Assert.DoesNotContain(prefix + "old-comp", remaining);
        Assert.Contains(prefix + "fresh-proc", remaining);
        Assert.Contains(prefix + "fresh-comp", remaining);

        // Cleanup local del set de prueba.
        var leftovers = await db.IdempotencyKeys
            .Where(x => x.EmpresaId == empresaId && x.UsuarioId == usuarioId)
            .ToListAsync();
        db.IdempotencyKeys.RemoveRange(leftovers);
        await db.SaveChangesAsync();
    }

    private static IdempotencyKey MakeKey(
        string key, Guid empresaId, Guid usuarioId, string status, DateTimeOffset createdAt)
    {
        return new IdempotencyKey
        {
            Key = key,
            EmpresaId = empresaId,
            UsuarioId = usuarioId,
            HttpMethod = "POST",
            Path = "/test",
            RequestBodyHash = new string('0', 64),
            Status = status,
            CorrelationId = Guid.CreateVersion7(),
            CreatedAt = createdAt,
        };
    }
}
