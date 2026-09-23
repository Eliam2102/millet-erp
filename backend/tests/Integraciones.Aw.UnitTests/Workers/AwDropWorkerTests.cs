using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Integraciones.Aw.UnitTests.Workers;

/// <summary>
/// Unit tests del flujo de mensajes del <see cref="AwDropWorker"/> en modo
/// per-EDI (PR #201). Usa el seam <c>AwDropWorker.ProcessRawMessageAsync</c>
/// + fake <see cref="IMessageCompletion"/> en lugar de Service Bus real,
/// y un <see cref="IntegracionesAwDbContext"/> backed por EF Core InMemory
/// (más fidelidad que un mock — verifica que SaveChanges efectivamente
/// persiste las transiciones).
///
/// <para>
/// <b>Cobertura:</b> los 4 outcomes (<see cref="DropOutcome.Success"/>,
/// <see cref="DropOutcome.Failed"/>, <see cref="DropOutcome.Stuck"/>,
/// <see cref="DropOutcome.Unknown"/>) + paths de error HTTP (transient /
/// permanent) + edge cases (payload corrupto, entidad ausente, EDI vacío,
/// idempotencia para entidad ya correlated).
/// </para>
/// </summary>
public sealed class AwDropWorkerTests
{
    private static readonly string[] FailCodes = ["1550", "1603", "1555"];

    [Fact]
    public async Task PayloadCorrupto_DeadLetter()
    {
        var (worker, _, completion) = BuildWorker((_, _) => { });

        await worker.ProcessRawMessageAsync("no es JSON válido {", "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.DeadLetter);
        completion.DeadLetterReason.Should().Be("InvalidPayload");
    }

    [Fact]
    public async Task EntidadNoEncontrada_DeadLetter()
    {
        var (worker, fakes, completion) = BuildWorker((_, _) => { /* DbContext vacío */ });
        var rawBody = SerializeEvent(NewEvent());

        await worker.ProcessRawMessageAsync(rawBody, "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.DeadLetter);
        completion.DeadLetterReason.Should().Be("AggregateNotFound");
        fakes.Adapter.SendCalls.Should().Be(0);
    }

    [Fact]
    public async Task IdempotentSkip_EntidadYaCorrelated_Complete()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, _) =>
        {
            var entidad = NewEntidad(evt);
            entidad.MarcarCorrelacionadaDirectamente(123L, DateTimeOffset.UtcNow);
            db.EntidadesExternas.Add(entidad);
            db.SaveChanges();
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        fakes.Adapter.SendCalls.Should().Be(0);
    }

    [Fact]
    public async Task EdiContentVacio_DeadLetter()
    {
        var evt = NewEvent();
        var (worker, _, completion) = BuildWorker((db, _) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt, ediContent: null));
            db.SaveChanges();
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.DeadLetter);
        completion.DeadLetterReason.Should().Be("MissingEdiContent");
    }

    [Fact]
    public async Task OutcomeSuccess_TransicionaACorrelated_PublishYNotify()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.SendResult = new DropResult(
                BytesWritten: 200, Path: @"E:\XML\Work\cot.edi", WrittenAt: DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Success,
                AwDocId: 10428001L,
                Lane: "default",
                WaitedMs: 3000);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        fakes.DbContext.EntidadesExternas.Single().Estado.Should().Be(EstadoEntidad.Correlated);
        fakes.DbContext.EntidadesExternas.Single().AwDocId.Should().Be(10428001L);
        fakes.Publisher.Published.Should().ContainSingle().Which.Should().BeOfType<AwPedidoCorrelacionado>();
        fakes.Notifier.CorrelacionExitosaCount.Should().Be(1);
    }

    [Fact]
    public async Task OutcomeSuccess_SinAwDocId_LogeaWarningYAvanzaIgual()
    {
        // Defensa en profundidad: si A+W no emitió (4614), drop service
        // reporta awDocId=null. El worker igual marca Correlated (el flow
        // funcionó técnicamente) pero loggea warning para detectar drift.
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.SendResult = new DropResult(100, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Success, AwDocId: null);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        fakes.DbContext.EntidadesExternas.Single().Estado.Should().Be(EstadoEntidad.Correlated);
        fakes.DbContext.EntidadesExternas.Single().AwDocId.Should().BeNull();
    }

    [Fact]
    public async Task OutcomeFailed_TransicionaAFailedCorrelation_ConCodigosYMensaje()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.SendResult = new DropResult(100, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Failed,
                AwDocId: 10428002L, // A+W reservó id antes de rechazar
                AwErrorCodes: FailCodes,
                AwErrorMessage: "artículo no existe; importación rechazada",
                AwDiagnosticLog: "(1555)...");
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        var persistida = fakes.DbContext.EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.FailedCorrelation);
        persistida.AwErrorCodes.Should().Be("1550,1603,1555");
        persistida.AwErrorMessage.Should().Contain("artículo");
        fakes.Publisher.Published.Should().ContainSingle().Which.Should().BeOfType<AwCotizacionRechazadaPorAw>();
        fakes.Notifier.EstadoActualizadoCount.Should().Be(1);
    }

    [Fact]
    public async Task OutcomeStuck_TransicionaAFailedDrop_KindAwProcessingTimeout()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.SendResult = new DropResult(100, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Stuck,
                Lane: "default",
                WaitedMs: 120000);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        var persistida = fakes.DbContext.EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.FailedDrop);
        persistida.LastError.Should().Contain("aw_processing_timeout");
        fakes.Publisher.Published.Should().ContainSingle().Which.Should().BeOfType<AwEdiEntregaFallida>();
    }

    [Fact]
    public async Task OutcomeUnknown_FallbackAFailedDropTerminal()
    {
        // Defensa: drop service viejo o response sin outcome legible →
        // FailedDrop con kind unknown_outcome. Operador investiga.
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.SendResult = new DropResult(100, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Unknown);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        var persistida = fakes.DbContext.EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.FailedDrop);
        persistida.LastError.Should().Contain("unknown_outcome");
    }

    [Fact]
    public async Task TransientHttpFail_Abandon_IncrementaRetryNoCambiaEstado()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.ThrowOnSend = new AwDropException("503 down", "http_5xx", isTransient: true);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Abandon);
        var persistida = fakes.DbContext.EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.Submitted); // no cambia
        persistida.RetryCount.Should().Be((short)1);
        fakes.Notifier.DropFallidoCount.Should().Be(1);
    }

    [Fact]
    public async Task PermanentHttpFail_DeadLetterYFailedDropTerminal()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.ThrowOnSend = new AwDropException("401", "http_401", isTransient: false);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.DeadLetter);
        completion.DeadLetterReason.Should().Be("http_401");
        fakes.DbContext.EntidadesExternas.Single().Estado.Should().Be(EstadoEntidad.FailedDrop);
        fakes.Notifier.DeadLetterCount.Should().Be(1);
    }

    [Fact]
    public async Task UnexpectedException_Abandon()
    {
        var evt = NewEvent();
        var (worker, _, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.ThrowOnSend = new InvalidOperationException("bug");
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Abandon);
    }

    [Fact]
    public async Task OutcomeSuccess_PersisteEnvioConFilenameYStatusSuccess()
    {
        // PR-1 (late-reconciliation): el worker registra cada drop en la
        // tabla envio con el filename del header X-Filename. El late-reconciler
        // matchea Processed\ por ese filename.
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.SendResult = new DropResult(200, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Success, AwDocId: 99L);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        var envio = fakes.DbContext.Envios.Single();
        envio.Filename.Should().Be(evt.FilenameSuggestion);
        envio.Status.Should().Be(EstadoEnvio.Success);
        envio.AttemptNumber.Should().Be((short)1);
        envio.BytesSent.Should().Be(200);
        envio.HttpStatusCode.Should().Be((short)200);
        envio.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task OutcomeStuck_PersisteEnvioConFilenameAunqueAwNoProceso()
    {
        // Caso crítico para late-reconciliation: aunque A+W no procesó
        // dentro del timeout (entidad → FailedDrop), el HTTP drop al
        // on-prem fue exitoso → Envio.Status=Success con filename. El
        // late-reconciler usa este filename para matchear Processed\
        // cuando A+W procese tarde.
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.SendResult = new DropResult(150, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Stuck, Lane: "default", WaitedMs: 120000);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        fakes.DbContext.EntidadesExternas.Single().Estado.Should().Be(EstadoEntidad.FailedDrop);
        var envio = fakes.DbContext.Envios.Single();
        envio.Filename.Should().Be(evt.FilenameSuggestion);
        envio.Status.Should().Be(EstadoEnvio.Success); // HTTP drop fue OK
    }

    [Fact]
    public async Task TransientHttpFail_PersisteEnvioConFilenameYStatusFailed()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            db.EntidadesExternas.Add(NewEntidad(evt));
            db.SaveChanges();
            adapter.ThrowOnSend = new AwDropException("503 down", "http_5xx", isTransient: true);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-1", completion, CancellationToken.None);

        var envio = fakes.DbContext.Envios.Single();
        envio.Filename.Should().Be(evt.FilenameSuggestion);
        envio.Status.Should().Be(EstadoEnvio.Failed);
        envio.ErrorKind.Should().Be("http_5xx");
        envio.ErrorMessage.Should().Contain("503");
    }

    [Fact]
    public async Task SegundoIntentoDesdeFailedDrop_AsignaAttemptNumber2()
    {
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            var entidad = NewEntidad(evt);
            entidad.MarcarDropFalladoTerminal("primer timeout", "aw_processing_timeout");
            db.EntidadesExternas.Add(entidad);

            // Sembramos el Envio del primer intento (lo que el worker habría
            // dejado en el flow anterior).
            db.Envios.Add(Envio.Empezar(
                entidad, attemptNumber: 1, startedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                dropServiceUrl: "http://test/drop", filename: "cot_CIR_Q-001-old.edi"));
            db.SaveChanges();

            adapter.SendResult = new DropResult(200, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Success, AwDocId: 1L);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-retry", completion, CancellationToken.None);

        var envios = fakes.DbContext.Envios.OrderBy(e => e.AttemptNumber).ToList();
        envios.Should().HaveCount(2);
        envios[1].AttemptNumber.Should().Be((short)2);
        envios[1].Status.Should().Be(EstadoEnvio.Success);
    }

    [Fact]
    public async Task RetryDesdeFailedDrop_ReintentaYTransicionaSegunOutcome()
    {
        // Cuando llega un mensaje para una entidad en FailedDrop (reintento
        // manual desde UI), el worker debe resetear contadores via Reintentar()
        // y procesar el outcome normalmente.
        var evt = NewEvent();
        var (worker, fakes, completion) = BuildWorker((db, adapter) =>
        {
            var entidad = NewEntidad(evt);
            entidad.IncrementarRetry("transient", "x");
            entidad.MarcarDropFalladoTerminal("max", "permanent");
            db.EntidadesExternas.Add(entidad);
            db.SaveChanges();

            adapter.SendResult = new DropResult(100, "/x", DateTimeOffset.UtcNow,
                Outcome: DropOutcome.Success, AwDocId: 42L);
        });

        await worker.ProcessRawMessageAsync(SerializeEvent(evt), "msg-retry", completion, CancellationToken.None);

        completion.Decision.Should().Be(CompletionDecision.Complete);
        var persistida = fakes.DbContext.EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.Correlated);
        persistida.AwDocId.Should().Be(42L);
        persistida.RetryCount.Should().Be((short)0); // reset por Reintentar()
    }

    // ─── Helpers ───

    private static AwCotizacionRecibida NewEvent() => new(
        EmpresaId: Guid.NewGuid(),
        OcurridoEn: DateTimeOffset.UtcNow,
        AggregateId: Guid.NewGuid(),
        QuoteReference: "Q-001",
        Sucursal: "CIR",
        FilenameSuggestion: "cot_CIR_Q-001.edi");

    private static EntidadExterna NewEntidad(AwCotizacionRecibida evt, string? ediContent = "EDI-PAYLOAD")
        => new(
            id: evt.AggregateId,
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: evt.QuoteReference,
            empresaId: evt.EmpresaId,
            payloadOriginal: "{}",
            ediContent: ediContent,
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: evt.Sucursal);

    private static string SerializeEvent(AwCotizacionRecibida evt) =>
        JsonSerializer.Serialize(evt);

    private static (AwDropWorker Worker, TestFakes Fakes, CapturingCompletion Completion) BuildWorker(
        Action<IntegracionesAwDbContext, FakeAdapter> setup)
    {
        // InMemory DB name único POR TEST (no por DbContext) para que
        // todas las scopes del mismo test compartan datos.
        var dbName = "aw-test-" + Guid.NewGuid().ToString("N");

        var services = new ServiceCollection();
        services.AddDbContext<IntegracionesAwDbContext>(opts => opts
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning)));

        var adapter = new FakeAdapter();
        var publisher = new FakePublisher();
        var notifier = new FakeNotifier();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var empresaContext = new FakeEmpresaContext();

        services.AddSingleton<IAwDropAdapter>(adapter);
        services.AddSingleton<IIntegrationEventPublisher>(publisher);
        services.AddSingleton<IIntegracionesAwNotifier>(notifier);
        services.AddSingleton<IAgentRealtimePublisher>(new FakeAgentRealtime());
        // RealRepo es scoped — EF resuelve el DbContext del scope correcto.
        services.AddScoped<IEntidadExternaRepository, RealRepo>();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICurrentEmpresaContext>(empresaContext);
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        // Seed dentro de un scope dedicado (se disposea al salir del using).
        using (var seedScope = scopeFactory.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
            setup(db, adapter);
        }

        var sb = new ServiceBusClient(
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=x");

        var worker = new AwDropWorker(
            sb, scopeFactory,
            Options.Create(new IntegracionesAwOptions()),
            new DocumentSyncKick(),
            NullLogger<AwDropWorker>.Instance);

        // Closure que abre un scope fresh para verificación — se invoca
        // tras ProcessRawMessageAsync. Cada llamada a FetchDb() devuelve
        // el DbContext de un nuevo scope; el caller lo usa para queries
        // y el scope se disposea cuando el GC libere el wrapper.
        Func<IntegracionesAwDbContext> fetchDb = () =>
        {
            var scope = scopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
        };

        return (worker,
            new TestFakes(fetchDb, adapter, publisher, notifier),
            new CapturingCompletion());
    }

    private sealed record TestFakes(
        Func<IntegracionesAwDbContext> FetchDbContext,
        FakeAdapter Adapter,
        FakePublisher Publisher,
        FakeNotifier Notifier)
    {
        // Atajo conveniente para los asserts.
        public IntegracionesAwDbContext DbContext => FetchDbContext();
    }

    // ─── Fakes ───

    private sealed class RealRepo : IEntidadExternaRepository
    {
        private readonly IntegracionesAwDbContext _db;

        public RealRepo(IntegracionesAwDbContext db)
        {
            _db = db;
        }

        public Task<EntidadExterna?> GetByIdAsync(Guid id, CancellationToken ct)
            => _db.EntidadesExternas.FirstOrDefaultAsync(e => e.Id == id, ct);

        public Task<EntidadExterna?> GetByIdCrossEmpresaAsync(Guid id, CancellationToken ct)
            => _db.EntidadesExternas.FirstOrDefaultAsync(e => e.Id == id, ct);

        public Task<EntidadExterna?> GetByReferenciaAsync(
            TipoEntidad t, string r, Guid empresaId, CancellationToken ct)
            => _db.EntidadesExternas.FirstOrDefaultAsync(
                e => e.TipoEntidad == t && e.ReferenciaExterna == r && e.EmpresaId == empresaId, ct);
    }

    private sealed class FakeAdapter : IAwDropAdapter
    {
        public int SendCalls;
        public DropResult SendResult { get; set; } = new(0, "", DateTimeOffset.UtcNow);
        public Exception? ThrowOnSend { get; set; }
        public List<string> ArchivedMarkers { get; } = new();

        public Task<DropResult> SendEdiAsync(string filename, string ediContent, CancellationToken ct)
        {
            SendCalls++;
            if (ThrowOnSend is not null) throw ThrowOnSend;
            return Task.FromResult(SendResult);
        }

        public Task ArchiveResultAsync(string markerName, CancellationToken ct)
        {
            ArchivedMarkers.Add(markerName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgentRealtime : IAgentRealtimePublisher
    {
        public int Calls;
        public Task PublishCotizacionActualizadaAsync(EntidadExterna entidad, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = new();
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : IIntegracionesAwNotifier
    {
        public int EstadoActualizadoCount;
        public int CorrelacionExitosaCount;
        public int CorrelacionExpiradaCount;
        public int DropFallidoCount;
        public int DeadLetterCount;

        public Task NotifyCotizacionEstadoActualizadoAsync(Guid empresaId, Guid agg, string qr,
            string estado, DateTimeOffset when, CancellationToken ct)
        { EstadoActualizadoCount++; return Task.CompletedTask; }

        public Task NotifyCorrelacionExitosaAsync(Guid empresaId, Guid agg, string qr, long awDocId,
            DateTimeOffset when, CancellationToken ct)
        { CorrelacionExitosaCount++; return Task.CompletedTask; }

        public Task NotifyCorrelacionExpiradaAsync(Guid empresaId, Guid agg, string qr,
            DateTimeOffset submittedAt, DateTimeOffset expiredAt, CancellationToken ct)
        { CorrelacionExpiradaCount++; return Task.CompletedTask; }

        public Task NotifyDropFallidoAsync(Guid empresaId, Guid agg, string qr,
            string errorMessage, string errorKind, int retryCount, CancellationToken ct)
        { DropFallidoCount++; return Task.CompletedTask; }

        public Task NotifyDropDeadLetterAsync(Guid empresaId, Guid agg, string qr,
            string errorMessage, string errorKind, CancellationToken ct)
        { DeadLetterCount++; return Task.CompletedTask; }

        public Task NotifyDocumentoAdjuntadoAsync(Guid empresaId, Guid agg, string qr,
            long awDocId, string docType, string pdfFilename, DateTimeOffset pdfUploadedAt,
            CancellationToken ct)
        { return Task.CompletedTask; }
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        // Bypass siempre activo: la suite de tests opera cross-empresa
        // sin distinguir empresas. Esto permite que seed/verify scopes
        // vean los datos sin necesidad de envolver cada query en Bypass().
        // El worker llama Bypass() de todas formas — pero como ya estamos
        // bypassed, es no-op.
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => NoopDisposable.Instance;

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }

    public enum CompletionDecision { None, Complete, Abandon, DeadLetter }

    public sealed class CapturingCompletion : IMessageCompletion
    {
        public CompletionDecision Decision { get; private set; } = CompletionDecision.None;
        public string? DeadLetterReason { get; private set; }

        public Task CompleteAsync(CancellationToken cancellationToken)
        { Decision = CompletionDecision.Complete; return Task.CompletedTask; }

        public Task AbandonAsync(CancellationToken cancellationToken)
        { Decision = CompletionDecision.Abandon; return Task.CompletedTask; }

        public Task DeadLetterAsync(string reason, string description, CancellationToken cancellationToken)
        { Decision = CompletionDecision.DeadLetter; DeadLetterReason = reason; return Task.CompletedTask; }
    }
}
