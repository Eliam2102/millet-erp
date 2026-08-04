using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Compras.IntegrationTests.Outbox;

/// <summary>
/// Tests integration de <see cref="OutboxPublisherWorker{TDbContext}"/>
/// (F6-PR2). Verifican que el worker:
/// <list type="bullet">
///   <item>Lee filas pendientes con <c>SELECT FOR UPDATE SKIP LOCKED</c>.</item>
///   <item>Invoca el sender; en éxito marca <c>PublishedAt</c>.</item>
///   <item>En fallo, incrementa <c>Attempts</c> y registra <c>LastError</c>;
///         no marca <c>PublishedAt</c>.</item>
///   <item>Cuando <c>Attempts &gt; MaxAttempts</c>, la fila queda excluida
///         del próximo poll (dead-letter pasivo).</item>
/// </list>
///
/// <para>
/// Usa un fake <see cref="IIntegrationEventBusSender"/> registrado por
/// <c>WithWebHostBuilder</c> para evitar Service Bus real. Llama
/// <c>PollOnceAsync</c> directamente (vía reflexión / tipo concreto)
/// para no depender del loop temporal del worker.
/// </para>
/// </summary>
public class OutboxPublisherWorkerTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;

    public OutboxPublisherWorkerTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // NOTA: el outbox es compartido entre tests; otras fixtures pueden
    // dejar filas con PublishedAt=NULL. Cada test asserta usando el
    // Id específico de las filas que sembró (no el total), y los
    // ThrowingSender / FilteringRecordingSender se filtran por
    // EventType-prefix único por test.

    [Fact]
    public async Task PollOnce_FilaPendiente_PublicaYMarca_PublishedAt()
    {
        var sender = new RecordingSender();
        var entry = await SeedOutboxEntryAsync();

        await using var factory = WithSender(sender);
        var worker = ResolveWorker(factory);

        await worker.PollOnceAsync(CancellationToken.None);

        // Sender recibió la fila específica que sembramos, y el topic
        // resuelto del worker<ComprasDbContext> es "compras-events"
        // (PR B — named options keyed por nameof(TDbContext)).
        Assert.Contains(sender.Sent, s => s.Entry.Id == entry.Id && s.TopicName == "compras-events");

        // BD: PublishedAt no nulo, Attempts 0, LastError nulo.
        var actual = await ReloadEntryAsync(factory, entry.Id);
        Assert.NotNull(actual);
        Assert.NotNull(actual!.PublishedAt);
        Assert.Equal(0, actual.Attempts);
        Assert.Null(actual.LastError);
    }

    [Fact]
    public async Task PollOnce_SenderFalla_IncrementaAttempts_Y_NoMarcaPublicado()
    {
        var sender = new ThrowingSender("transient bus error");
        var entry = await SeedOutboxEntryAsync();

        await using var factory = WithSender(sender);
        var worker = ResolveWorker(factory);

        await worker.PollOnceAsync(CancellationToken.None);

        var actual = await ReloadEntryAsync(factory, entry.Id);
        Assert.NotNull(actual);
        Assert.Null(actual!.PublishedAt);
        Assert.Equal(1, actual.Attempts);
        Assert.Contains("transient bus error", actual.LastError);
    }

    [Fact]
    public async Task PollOnce_FilaConAttemptsExcedeMaxAttempts_NoSeReintenta()
    {
        // MaxAttempts default = 10. Forzamos attempts=11 antes del poll.
        var sender = new RecordingSender();
        var entry = await SeedOutboxEntryAsync();
        await UpdateAttemptsAsync(entry.Id, 11);

        await using var factory = WithSender(sender);
        var worker = ResolveWorker(factory);

        await worker.PollOnceAsync(CancellationToken.None);

        // Sender NO recibió ESTA fila específica (otras filas pendientes
        // de tests previos en BD compartida sí pueden ser procesadas).
        Assert.DoesNotContain(sender.Sent, s => s.Entry.Id == entry.Id);

        var actual = await ReloadEntryAsync(factory, entry.Id);
        Assert.Null(actual!.PublishedAt);
        Assert.Equal(11, actual.Attempts); // sin cambio
    }

    [Fact]
    public async Task PollOnce_VariasFilas_LasProcesaTodas()
    {
        var sender = new RecordingSender();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var entry = await SeedOutboxEntryAsync();
            ids.Add(entry.Id);
        }

        await using var factory = WithSender(sender);
        var worker = ResolveWorker(factory);

        await worker.PollOnceAsync(CancellationToken.None);

        // Las 5 filas que sembramos están en lo procesado por el sender.
        foreach (var id in ids)
        {
            Assert.Contains(sender.Sent, s => s.Entry.Id == id);
        }
    }

    // --- Helpers ---

    private WebApplicationFactory<Program> WithSender(IIntegrationEventBusSender sender) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIntegrationEventBusSender>();
                services.AddSingleton(sender);

                // Quitar el OutboxPublisherWorker registrado como
                // BackgroundService: en tests construimos el worker
                // manualmente para llamar PollOnceAsync directamente.
                // Si dejamos el hosted service activo, su loop de
                // Task.Delay tickea en paralelo con nuestro PollOnceAsync
                // y procesa la misma fila dos veces (Attempts=2 en lugar
                // de 1, etc.).
                var workerDescriptors = services
                    .Where(d => d.ImplementationType == typeof(OutboxPublisherWorker<ComprasDbContext>))
                    .ToList();
                foreach (var d in workerDescriptors) services.Remove(d);
            });
        });

    /// <summary>
    /// Construye un <c>OutboxPublisherWorker&lt;ComprasDbContext&gt;</c>
    /// manualmente con las dependencias del DI del factory. Evita el
    /// loop del BackgroundService (lo removimos en <see cref="WithSender"/>)
    /// para que el test controle exactamente cuándo se invoca el tick.
    /// </summary>
    private static OutboxPublisherWorker<ComprasDbContext> ResolveWorker(WebApplicationFactory<Program> factory)
    {
        var scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();
        // PR B refactor: el worker resuelve sus options via IOptionsMonitor
        // por nombre del tipo del DbContext. Replicamos esa resolución aquí
        // para construir el worker manualmente (sin el BackgroundService loop).
        var optionsMonitor = factory.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>();
        var logger = factory.Services.GetRequiredService<ILogger<OutboxPublisherWorker<ComprasDbContext>>>();
        return new OutboxPublisherWorker<ComprasDbContext>(scopeFactory, optionsMonitor, logger);
    }

    private async Task<IntegrationEventOutboxEntry> SeedOutboxEntryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var marker = $"f6pr2-test-{Guid.NewGuid():N}";
        var entry = new IntegrationEventOutboxEntry(
            id: Guid.CreateVersion7(),
            eventType: $"test.outbox.worker.{marker}.v1",
            payload: $"{{\"marker\":\"{marker}\"}}",
            occurredAt: DateTimeOffset.UtcNow,
            empresaId: Guid.CreateVersion7());

        db.OutboxEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    private async Task UpdateAttemptsAsync(Guid entryId, int attempts)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE compras.integration_events_outbox SET attempts = {attempts} WHERE id = {entryId}");
    }

    private static async Task<IntegrationEventOutboxEntry?> ReloadEntryAsync(
        WebApplicationFactory<Program> factory,
        Guid entryId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        return await db.OutboxEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entryId);
    }

    // --- Test doubles para el sender ---

    private sealed class RecordingSender : IIntegrationEventBusSender
    {
        public List<(IntegrationEventOutboxEntry Entry, string TopicName)> Sent { get; } = new();

        public Task SendAsync(
            IntegrationEventOutboxEntry entry,
            string topicName,
            CancellationToken cancellationToken)
        {
            Sent.Add((entry, topicName));
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSender : IIntegrationEventBusSender
    {
        private readonly string _message;
        public ThrowingSender(string message) { _message = message; }

        public Task SendAsync(
            IntegrationEventOutboxEntry entry,
            string topicName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(_message);
    }
}
