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
/// Tests del <see cref="AwLateReconciliationWorker"/>. Usan el seam
/// <c>ReconcileOnceAsync</c> + InMemory DB para verificar la lógica del
/// ciclo sin levantar el host real.
/// </summary>
public sealed class AwLateReconciliationWorkerTests
{
    private static readonly string[] FailCodes = ["1555"];
    private static readonly string[] EmptyCodes = [];

    [Fact]
    public async Task ReconcileOnce_SinCandidatas_NoLlamaReader()
    {
        var (worker, fakes) = await BuildAsync(seed: _ => { });

        await worker.ReconcileOnceAsync(CancellationToken.None);

        fakes.Reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ReconcileOnce_CotizacionFailedDropConTimeoutYFilenameMatchSuccess_TransitaACorrelated()
    {
        var entidadId = Guid.NewGuid();
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(entidadId);
            entidad.MarcarStuck(120000, "default");
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, attemptNumber: 1,
                startedAt: NowMinutes(-5), filename: "cot_CIR_Q-LATE.edi"));
            db.SaveChanges();
        });

        fakes.Reader.NextPage = new AwCompletionsPage(
            Completions: new List<AwCompletionItem>
            {
                new(
                    Filename: "cot_CIR_Q-LATE.edi",
                    Outcome: DropOutcome.Success,
                    AwDocId: 9999L,
                    ErrorCodes: EmptyCodes,
                    ErrorMessage: null,
                    DiagnosticLog: "(4615) ...",
                    ParsedAt: NowMinutes(-1),
                    Lane: "default"),
            },
            NextSince: NowMinutes(-1));

        await worker.ReconcileOnceAsync(CancellationToken.None);

        var persistida = fakes.FetchDb().EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.Correlated);
        persistida.AwDocId.Should().Be(9999L);
        fakes.Publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<AwPedidoCorrelacionado>();
        fakes.Notifier.CorrelacionExitosaCount.Should().Be(1);
    }

    [Fact]
    public async Task ReconcileOnce_MatchFailed_TransitaAFailedCorrelation()
    {
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid());
            entidad.MarcarStuck(120000);
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, 1, NowMinutes(-5), "cot_CIR_Q-FAIL.edi"));
            db.SaveChanges();
        });

        fakes.Reader.NextPage = new AwCompletionsPage(
            new[]
            {
                new AwCompletionItem(
                    Filename: "cot_CIR_Q-FAIL.edi",
                    Outcome: DropOutcome.Failed,
                    AwDocId: null,
                    ErrorCodes: FailCodes,
                    ErrorMessage: "rechazado tarde",
                    DiagnosticLog: "(1555) ...",
                    ParsedAt: NowMinutes(-1),
                    Lane: "default"),
            },
            NextSince: NowMinutes(-1));

        await worker.ReconcileOnceAsync(CancellationToken.None);

        var persistida = fakes.FetchDb().EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.FailedCorrelation);
        persistida.AwErrorCodes.Should().Be("1555");
        fakes.Publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<AwCotizacionRechazadaPorAw>();
    }

    [Fact]
    public async Task ReconcileOnce_MatchStuck_NoTransita_EsperaProximoCiclo()
    {
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid());
            entidad.MarcarStuck(120000);
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, 1, NowMinutes(-5), "cot_CIR_Q-STUCK.edi"));
            db.SaveChanges();
        });

        fakes.Reader.NextPage = new AwCompletionsPage(
            new[]
            {
                new AwCompletionItem(
                    Filename: "cot_CIR_Q-STUCK.edi",
                    Outcome: DropOutcome.Stuck,
                    AwDocId: null,
                    ErrorCodes: EmptyCodes,
                    ErrorMessage: null,
                    DiagnosticLog: null,
                    ParsedAt: NowMinutes(-1),
                    Lane: "default"),
            },
            NextSince: NowMinutes(-1));

        await worker.ReconcileOnceAsync(CancellationToken.None);

        var persistida = fakes.FetchDb().EntidadesExternas.Single();
        persistida.Estado.Should().Be(EstadoEntidad.FailedDrop); // sigue stuck
        fakes.Publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileOnce_FilenameSinMatchEnReader_NoTransita()
    {
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid());
            entidad.MarcarStuck(120000);
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, 1, NowMinutes(-5), "cot_CIR_Q-MISS.edi"));
            db.SaveChanges();
        });

        fakes.Reader.NextPage = new AwCompletionsPage(
            new[]
            {
                new AwCompletionItem("otro.edi", DropOutcome.Success, 1L,
                    EmptyCodes, null, null, NowMinutes(-1), "default"),
            },
            NextSince: NowMinutes(-1));

        await worker.ReconcileOnceAsync(CancellationToken.None);

        fakes.FetchDb().EntidadesExternas.Single().Estado.Should().Be(EstadoEntidad.FailedDrop);
    }

    [Fact]
    public async Task ReconcileOnce_KindNoTimeout_NoSeIncluyeEnCandidatas()
    {
        // FailedDrop por http_401 no debe considerarse — solo timeouts.
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid());
            entidad.MarcarDropFalladoTerminal("401", "http_401");
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, 1, NowMinutes(-5), "cot_CIR_Q-401.edi"));
            db.SaveChanges();
        });

        await worker.ReconcileOnceAsync(CancellationToken.None);

        fakes.Reader.Calls.Should().Be(0); // sin candidatas, no se llama al reader
    }

    [Fact]
    public async Task ReconcileOnce_EstadoNoFailedDrop_NoSeIncluye()
    {
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid()); // Submitted
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, 1, NowMinutes(-5), "cot_CIR_Q-OK.edi"));
            db.SaveChanges();
        });

        await worker.ReconcileOnceAsync(CancellationToken.None);

        fakes.Reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ReconcileOnce_EnvioFueraDeVentana_NoSeReconcilia()
    {
        // Última StartedAt = -30h, ventana default 24h → fuera de ventana.
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid());
            entidad.MarcarStuck(120000);
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, 1,
                startedAt: DateTimeOffset.UtcNow.AddHours(-30),
                filename: "cot_CIR_Q-OLD.edi"));
            db.SaveChanges();
        });

        await worker.ReconcileOnceAsync(CancellationToken.None);

        // No hay candidatas dentro de ventana → no llama reader.
        fakes.Reader.Calls.Should().Be(0);
        fakes.FetchDb().EntidadesExternas.Single().Estado.Should().Be(EstadoEntidad.FailedDrop);
    }

    [Fact]
    public async Task ReconcileOnce_SinEnvioConFilename_NoSeIncluye()
    {
        // FailedDrop con timeout pero sin Envio persistido (legacy pre-PR-1).
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid());
            entidad.MarcarStuck(120000);
            db.EntidadesExternas.Add(entidad);
            // sin db.Envios.Add(...)
            db.SaveChanges();
        });

        await worker.ReconcileOnceAsync(CancellationToken.None);

        fakes.Reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ReconcileOnce_VariosFilenames_LeQueDevuelveReaderPaginaCompartida()
    {
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var a = NewEntidad(Guid.NewGuid(), "Q-A");
            var b = NewEntidad(Guid.NewGuid(), "Q-B");
            a.MarcarStuck(120000);
            b.MarcarStuck(120000);
            db.EntidadesExternas.AddRange(a, b);
            db.Envios.Add(EnvioStarted(a.Id, 1, NowMinutes(-10), "cot_CIR_Q-A.edi"));
            db.Envios.Add(EnvioStarted(b.Id, 1, NowMinutes(-5), "cot_CIR_Q-B.edi"));
            db.SaveChanges();
        });

        fakes.Reader.NextPage = new AwCompletionsPage(
            new[]
            {
                new AwCompletionItem("cot_CIR_Q-A.edi", DropOutcome.Success, 1L,
                    EmptyCodes, null, null, NowMinutes(-2), "default"),
                new AwCompletionItem("cot_CIR_Q-B.edi", DropOutcome.Success, 2L,
                    EmptyCodes, null, null, NowMinutes(-1), "default"),
            },
            NextSince: NowMinutes(-1));

        await worker.ReconcileOnceAsync(CancellationToken.None);

        var persistidas = fakes.FetchDb().EntidadesExternas.ToList();
        persistidas.Should().AllSatisfy(e => e.Estado.Should().Be(EstadoEntidad.Correlated));
        var docIds = persistidas.Select(e => e.AwDocId).OrderBy(x => x).ToList();
        docIds.Should().BeEquivalentTo(new long?[] { 1L, 2L });
        fakes.Reader.Calls.Should().Be(1); // una sola llamada al reader
    }

    [Fact]
    public async Task ReconcileOnce_FilenameDuplicadoEnPagina_UsaElParseAtMasReciente()
    {
        var (worker, fakes) = await BuildAsync(seed: db =>
        {
            var entidad = NewEntidad(Guid.NewGuid());
            entidad.MarcarStuck(120000);
            db.EntidadesExternas.Add(entidad);
            db.Envios.Add(EnvioStarted(entidad.Id, 1, NowMinutes(-5), "cot_CIR_Q-DUP.edi"));
            db.SaveChanges();
        });

        // Dos entries con el mismo filename, distinto ParsedAt — quedaría el más reciente.
        fakes.Reader.NextPage = new AwCompletionsPage(
            new[]
            {
                new AwCompletionItem("cot_CIR_Q-DUP.edi", DropOutcome.Success, 111L,
                    EmptyCodes, null, null, NowMinutes(-3), "default"),
                new AwCompletionItem("cot_CIR_Q-DUP.edi", DropOutcome.Success, 222L,
                    EmptyCodes, null, null, NowMinutes(-1), "default"),
            },
            NextSince: NowMinutes(-1));

        await worker.ReconcileOnceAsync(CancellationToken.None);

        fakes.FetchDb().EntidadesExternas.Single().AwDocId.Should().Be(222L);
    }

    // ─── helpers ───

    private static EntidadExterna NewEntidad(Guid id, string quoteRef = "Q-001") => new(
        id: id,
        tipoEntidad: TipoEntidad.Cotizacion,
        referenciaExterna: quoteRef,
        empresaId: Guid.NewGuid(),
        payloadOriginal: "{}",
        ediContent: "EDI",
        submittedBySpnId: null,
        submittedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
        sucursal: "CIR");

    private static Envio EnvioStarted(Guid entidadId, short attemptNumber,
        DateTimeOffset startedAt, string filename)
    {
        // No tenemos referencia a EntidadExterna trackeada en este helper; el
        // factory exige la entidad. Construimos una stub solo para pasar el
        // ArgumentNullCheck — el repositorio guarda por EntidadExternaId.
        var stubEntidad = new EntidadExterna(
            id: entidadId,
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-stub",
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: null,
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");
        return Envio.Empezar(stubEntidad, attemptNumber, startedAt,
            "http://test/drop", filename);
    }

    private static DateTimeOffset NowMinutes(int delta) =>
        DateTimeOffset.UtcNow.AddMinutes(delta);

    private static Task<(AwLateReconciliationWorker Worker, TestFakes Fakes)> BuildAsync(
        Action<IntegracionesAwDbContext> seed)
    {
        var dbName = "aw-late-" + Guid.NewGuid().ToString("N");

        var services = new ServiceCollection();
        services.AddDbContext<IntegracionesAwDbContext>(opts => opts
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning)));

        var reader = new FakeCompletionsReader();
        var publisher = new FakePublisher();
        var notifier = new FakeNotifier();
        var agentRealtime = new FakeAgentRealtime();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var empresaContext = new FakeEmpresaContext();

        services.AddSingleton<IAwCompletionsReader>(reader);
        services.AddSingleton<IAwDropAdapter>(new NoopDropAdapter());
        services.AddSingleton<IIntegrationEventPublisher>(publisher);
        services.AddSingleton<IIntegracionesAwNotifier>(notifier);
        services.AddSingleton<IAgentRealtimePublisher>(agentRealtime);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<ICurrentEmpresaContext>(empresaContext);
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        using (var seedScope = scopeFactory.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
            seed(db);
        }

        var worker = new AwLateReconciliationWorker(
            scopeFactory,
            Options.Create(new IntegracionesAwOptions()),
            new DocumentSyncKick(),
            NullLogger<AwLateReconciliationWorker>.Instance);

        Func<IntegracionesAwDbContext> fetchDb = () =>
            scopeFactory.CreateScope().ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();

        return Task.FromResult((worker, new TestFakes(fetchDb, reader, publisher, notifier)));
    }

    private sealed record TestFakes(
        Func<IntegracionesAwDbContext> FetchDb,
        FakeCompletionsReader Reader,
        FakePublisher Publisher,
        FakeNotifier Notifier);

    // ─── fakes ───

    private sealed class NoopDropAdapter : IAwDropAdapter
    {
        public List<string> Archived { get; } = new();
        public Task<DropResult> SendEdiAsync(string filename, string ediContent, CancellationToken ct) =>
            Task.FromResult(new DropResult(0, string.Empty, DateTimeOffset.UtcNow));
        public Task ArchiveResultAsync(string markerName, CancellationToken ct)
        {
            Archived.Add(markerName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCompletionsReader : IAwCompletionsReader
    {
        public int Calls;
        public AwCompletionsPage NextPage { get; set; } =
            new(Array.Empty<AwCompletionItem>(), NextSince: null);
        public Exception? ThrowOnList { get; set; }

        public Task<AwCompletionsPage> ListSinceAsync(
            DateTimeOffset? since, int limit, CancellationToken ct)
        {
            Calls++;
            if (ThrowOnList is not null) throw ThrowOnList;
            return Task.FromResult(NextPage);
        }
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = new();
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
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

    private sealed class FakeAgentRealtime : IAgentRealtimePublisher
    {
        public int Calls;
        public Task PublishCotizacionActualizadaAsync(EntidadExterna entidad, CancellationToken ct)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset now) { UtcNow = now; }
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => NoopDisposable.Instance;

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
