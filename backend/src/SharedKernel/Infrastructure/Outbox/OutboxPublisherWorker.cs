using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// <c>BackgroundService</c> que polea el outbox de un <c>TDbContext</c>
/// específico, publica los eventos pendientes vía
/// <see cref="IIntegrationEventBusSender"/>, y marca la fila como
/// publicada (F6-PR2, ADR-0009).
///
/// <para>
/// Genérico sobre <typeparamref name="TDbContext"/>: cada módulo que
/// integra registra su propio worker:
/// <c>services.AddHostedService&lt;OutboxPublisherWorker&lt;ComprasDbContext&gt;&gt;()</c>.
/// El worker lee el schema-qualified table name del modelo EF; no
/// hardcodea nombres.
/// </para>
/// <para>
/// Usa <c>SELECT ... FOR UPDATE SKIP LOCKED</c> para que múltiples
/// instancias del worker no procesen la misma fila (escalabilidad
/// horizontal). Las filas con <c>Attempts &gt; MaxAttempts</c> se
/// excluyen del polling (dead-letter pasivo).
/// </para>
/// <para>
/// Se ejecuta dentro de una TX EF que el using-dispose commitea o
/// rollbackea: si Service Bus falla, el <c>Attempts</c> se incrementa
/// y se persiste; si SaveChanges falla, los locks se liberan y la fila
/// se vuelve a tomar en el próximo poll.
/// </para>
/// </summary>
public sealed class OutboxPublisherWorker<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxPublisherOptions _options;
    private readonly ILogger<OutboxPublisherWorker<TDbContext>> _logger;

    private string? _qualifiedTableName;

    /// <summary>
    /// Construye el worker resolviendo las options por nombre del tipo
    /// del DbContext (PR B — Opción D del análisis OutboxPublisherOptions
    /// keying). Cada módulo registra sus options con:
    /// <c>services.AddOptions&lt;OutboxPublisherOptions&gt;(nameof(MyDbContext)).Bind(...)</c>.
    /// Si el operador olvida registrar las options para el
    /// <typeparamref name="TDbContext"/>, el constructor lanza
    /// <see cref="InvalidOperationException"/> al arranque — fail-fast
    /// en lugar de bug silencioso (publicar al topic equivocado).
    /// </summary>
    public OutboxPublisherWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<OutboxPublisherOptions> optionsMonitor,
        ILogger<OutboxPublisherWorker<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var optionsName = typeof(TDbContext).Name;
        _options = optionsMonitor.Get(optionsName);

        // Fail-fast si las options no se registraron para este DbContext
        // (CreateNamedOptions devuelve defaults vacíos en ese caso).
        if (string.IsNullOrWhiteSpace(_options.ServiceBusTopicName))
        {
            throw new InvalidOperationException(
                $"OutboxPublisherOptions no configuradas para '{optionsName}'. " +
                $"Verifica que services.AddOptions<OutboxPublisherOptions>(\"{optionsName}\").Bind(...) " +
                "esté en Program.cs y apunte a una sección con ServiceBusTopicName poblado.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation(
                "OutboxPublisherWorker<{DbContext}> está deshabilitado (Outbox:Disabled=true). Loop no inicia.",
                typeof(TDbContext).Name);
            return;
        }

        _logger.LogInformation(
            "OutboxPublisherWorker<{DbContext}> iniciado. PollInterval={PollSeconds}s BatchSize={BatchSize} MaxAttempts={MaxAttempts}",
            typeof(TDbContext).Name,
            _options.PollIntervalSeconds,
            _options.BatchSize,
            _options.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en OutboxPublisherWorker. Continuando tras intervalo.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Procesa un tick del worker. Expuesto como <c>internal</c> para
    /// que tests integration puedan ejecutar un solo tick sin esperar
    /// el delay loop.
    /// </summary>
    internal async Task PollOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<TDbContext>();
        var sender = sp.GetRequiredService<IIntegrationEventBusSender>();
        var clock = sp.GetRequiredService<IClock>();

        var qualifiedTable = ResolveQualifiedTableName(db);

        await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);

        // FOR UPDATE SKIP LOCKED: lock las filas en este worker; otros
        // workers / instancias toman las siguientes. Los locks se
        // liberan en commit/rollback del using.
        // OrderBy occurred_at: FIFO — eventos antiguos primero.
        var sql = $$"""
            SELECT * FROM {{qualifiedTable}}
            WHERE published_at IS NULL AND attempts <= {0}
            ORDER BY occurred_at
            LIMIT {1}
            FOR UPDATE SKIP LOCKED
            """;

        var entries = await db.Set<IntegrationEventOutboxEntry>()
            .FromSqlRaw(sql, _options.MaxAttempts, _options.BatchSize)
            .ToListAsync(stoppingToken);

        if (entries.Count == 0)
        {
            await tx.CommitAsync(stoppingToken);
            return;
        }

        _logger.LogDebug("Procesando {Count} eventos de outbox.", entries.Count);

        foreach (var entry in entries)
        {
            try
            {
                await sender.SendAsync(entry, _options.ServiceBusTopicName, stoppingToken);
                entry.MarcarPublicado(clock.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                entry.RegistrarFalla(ex.Message);

                if (entry.Attempts > _options.MaxAttempts)
                {
                    _logger.LogError(ex,
                        "Evento {EventType} id={Id} excedió MaxAttempts ({MaxAttempts}). Queda como dead-letter pasivo en outbox.",
                        entry.EventType, entry.Id, _options.MaxAttempts);
                }
                else
                {
                    _logger.LogWarning(ex,
                        "Falla publicando evento {EventType} id={Id}. Attempts={Attempts}/{MaxAttempts}. Reintento en próximo poll.",
                        entry.EventType, entry.Id, entry.Attempts, _options.MaxAttempts);
                }
            }
        }

        await db.SaveChangesAsync(stoppingToken);
        await tx.CommitAsync(stoppingToken);
    }

    /// <summary>
    /// Resuelve y cachea el nombre schema-qualificado de la tabla del
    /// outbox (ej. <c>compras.integration_events_outbox</c>) leyendo
    /// el modelo EF del <typeparamref name="TDbContext"/>.
    /// </summary>
    private string ResolveQualifiedTableName(DbContext db)
    {
        if (_qualifiedTableName is not null) return _qualifiedTableName;

        var entityType = db.Model.FindEntityType(typeof(IntegrationEventOutboxEntry))
            ?? throw new InvalidOperationException(
                $"DbContext {typeof(TDbContext).Name} no mapea {nameof(IntegrationEventOutboxEntry)}; el worker no puede polear.");

        var schema = entityType.GetSchema();
        var table = entityType.GetTableName()
            ?? throw new InvalidOperationException(
                $"{nameof(IntegrationEventOutboxEntry)} no tiene table name configurado en {typeof(TDbContext).Name}.");

        _qualifiedTableName = string.IsNullOrEmpty(schema)
            ? $"\"{table}\""
            : $"\"{schema}\".\"{table}\"";
        return _qualifiedTableName;
    }
}
