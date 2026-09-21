using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Ports.Blob;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Integraciones.Aw.UnitTests.Workers;

/// <summary>
/// Tests del <see cref="AwDocumentSyncWorker"/>. Usan el seam
/// <c>SyncOnceAsync</c> + InMemory DB para verificar el emparejamiento
/// (tipo, aw_doc_id), la descarga/subida a blob y la idempotencia, sin
/// levantar el host real.
/// </summary>
public sealed class AwDocumentSyncWorkerTests
{
    [Fact]
    public async Task SyncOnce_SinDocumentos_NoHaceNada()
    {
        // Doc-list-driven: el reader SÍ se consulta (es el driver), pero si la
        // carpeta caliente está vacía no hay subidas ni archivados.
        var (worker, fakes) = Build(seed: _ => { });

        await worker.SyncOnceAsync(CancellationToken.None);

        fakes.Reader.ListCalls.Should().Be(1);
        fakes.Blob.Uploads.Should().BeEmpty();
        fakes.Reader.Archived.Should().BeEmpty();
    }

    [Fact]
    public async Task SyncOnce_CorrelacionRecienteSinPdf_DevuelvePendienteCaliente()
    {
        // Correlacionada hace 0h (dentro de la ventana caliente de 5min) y sin
        // PDF (carpeta vacía) → SyncOnce devuelve 1 → el loop poleará rápido.
        var (worker, _) = Build(seed: db =>
        {
            db.EntidadesExternas.Add(
                Correlacionada(Guid.NewGuid(), TipoEntidad.Cotizacion, awDocId: 4614, correlatedHoursAgo: 0));
            db.SaveChanges();
        });

        var pending = await worker.SyncOnceAsync(CancellationToken.None);

        pending.Should().Be(1);
    }

    [Fact]
    public async Task SyncOnce_CorrelacionViejaSinPdf_NoCuentaComoPendienteCaliente()
    {
        // Correlacionada hace 1h (fuera de la ventana de 5min) → no mantiene
        // el poll rápido; el barrido idle + LookbackHours la cubren.
        var (worker, _) = Build(seed: db =>
        {
            db.EntidadesExternas.Add(
                Correlacionada(Guid.NewGuid(), TipoEntidad.Cotizacion, awDocId: 4614, correlatedHoursAgo: 1));
            db.SaveChanges();
        });

        var pending = await worker.SyncOnceAsync(CancellationToken.None);

        pending.Should().Be(0);
    }

    [Fact]
    public async Task SyncOnce_OfertaMatcheaCotizacion_AdjuntaPdfYNotifica()
    {
        var id = Guid.NewGuid();
        var (worker, fakes) = Build(seed: db =>
        {
            db.EntidadesExternas.Add(Correlacionada(id, TipoEntidad.Cotizacion, awDocId: 4614));
            db.SaveChanges();
        });

        var modifiedAt = NowMinutes(-10);
        fakes.Reader.NextPage = OnePage(
            new AwDocumentItem("oferta_4614.pdf", "oferta", 4614L, 1024, modifiedAt));

        await worker.SyncOnceAsync(CancellationToken.None);

        var persistida = fakes.FetchDb().EntidadesExternas.Single();
        persistida.PdfBlobUrl.Should().NotBeNull();
        persistida.PdfFilename.Should().Be("oferta_4614.pdf");
        // pdf_uploaded_at = modified_at REAL del archivo, NO la correlación ni now.
        persistida.PdfUploadedAt.Should().Be(modifiedAt);
        persistida.PdfUploadedAt.Should().BeAfter(persistida.CorrelatedAt!.Value);
        // El estado de correlación NO cambia: el PDF es un adjunto ortogonal.
        persistida.Estado.Should().Be(EstadoEntidad.Correlated);

        fakes.Reader.Downloaded.Should().ContainSingle().Which.Should().Be("oferta_4614.pdf");
        fakes.Blob.Uploads.Should().ContainSingle();
        fakes.Publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<AwDocumentoAdjuntado>();
        fakes.Notifier.DocumentoAdjuntadoCount.Should().Be(1);
        fakes.AgentRealtime.Calls.Should().Be(1);
        // Webhook live al Agent, una sola vez, al adjuntar.
        fakes.AgentWebhook.Calls.Should().Be(1);
        // Tras subir+persistir, se archiva (ack) para sacarlo de la caliente.
        fakes.Reader.Archived.Should().ContainSingle().Which.Should().Be("oferta_4614.pdf");
    }

    [Fact]
    public async Task SyncOnce_PedidoMatcheaCotizacionPorAwDocId_Adjunta()
    {
        // El ERP guarda TODA entidad como Cotizacion (no crea Pedido). Un PDF
        // de PEDIDO debe emparejar con su entidad Cotizacion por aw_doc_id —
        // el doc_type del PDF no corresponde al TipoEntidad. (Regresión: antes
        // se emparejaba pedido→TipoEntidad.Pedido y los pedidos nunca se adjuntaban.)
        var id = Guid.NewGuid();
        var (worker, fakes) = Build(seed: db =>
        {
            db.EntidadesExternas.Add(Correlacionada(id, TipoEntidad.Cotizacion, awDocId: 40217236));
            db.SaveChanges();
        });

        fakes.Reader.NextPage = OnePage(
            new AwDocumentItem("pedido_40217236.pdf", "pedido", 40217236L, 2048, NowMinutes(-5)));

        await worker.SyncOnceAsync(CancellationToken.None);

        var persistida = fakes.FetchDb().EntidadesExternas.Single();
        persistida.PdfFilename.Should().Be("pedido_40217236.pdf");
        persistida.PdfBlobUrl.Should().NotBeNull();
        fakes.Reader.Archived.Should().ContainSingle().Which.Should().Be("pedido_40217236.pdf");
    }

    [Fact]
    public async Task SyncOnce_YaTienePdf_NoReSubePeroReArchiva()
    {
        // Si un ack previo se perdió, el doc sigue en la carpeta caliente y se
        // re-lista. La entidad ya tiene PdfBlobUrl → NO se re-sube, pero SÍ se
        // re-archiva (idempotente del lado del drop service).
        var id = Guid.NewGuid();
        var (worker, fakes) = Build(seed: db =>
        {
            db.EntidadesExternas.Add(Correlacionada(id, TipoEntidad.Cotizacion, 4614));
            db.SaveChanges();
        });
        fakes.Reader.NextPage = OnePage(
            new AwDocumentItem("oferta_4614.pdf", "oferta", 4614L, 1024, NowMinutes(-5)));

        await worker.SyncOnceAsync(CancellationToken.None); // sube + archiva
        fakes.Blob.Uploads.Should().HaveCount(1);
        fakes.Reader.Archived.Should().HaveCount(1);

        await worker.SyncOnceAsync(CancellationToken.None); // el fake re-lista el mismo doc

        // No re-subió (idempotencia por PdfBlobUrl), pero re-archivó.
        fakes.Blob.Uploads.Should().HaveCount(1);
        fakes.Reader.Downloaded.Should().HaveCount(1);
        fakes.Reader.Archived.Should().HaveCount(2);
        // El webhook se dispara UNA sola vez (solo en el primer adjuntado).
        fakes.AgentWebhook.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SyncOnce_DocumentoSinEntidad_NoSubeNiArchiva()
    {
        // PDF para un aw_doc_id sin entidad (todavía no correlacionada o nunca
        // pasó por el Agent): se deja en la carpeta caliente para más tarde.
        var (worker, fakes) = Build(seed: _ => { });

        fakes.Reader.NextPage = OnePage(
            new AwDocumentItem("oferta_4614.pdf", "oferta", 4614L, 1024, NowMinutes(-5)));

        await worker.SyncOnceAsync(CancellationToken.None);

        fakes.Blob.Uploads.Should().BeEmpty();
        fakes.Reader.Archived.Should().BeEmpty();
        fakes.AgentWebhook.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SyncOnce_DescargaFalla_NoAdjuntaNiArchiva()
    {
        var (worker, fakes) = Build(seed: db =>
        {
            db.EntidadesExternas.Add(Correlacionada(Guid.NewGuid(), TipoEntidad.Cotizacion, 4614));
            db.SaveChanges();
        });
        fakes.Reader.NextPage = OnePage(
            new AwDocumentItem("oferta_4614.pdf", "oferta", 4614L, 1024, NowMinutes(-5)));
        fakes.Reader.ThrowOnDownload = new AwDocumentsException("boom", "network", isTransient: true);

        await worker.SyncOnceAsync(CancellationToken.None);

        fakes.FetchDb().EntidadesExternas.Single().PdfBlobUrl.Should().BeNull();
        fakes.Blob.Uploads.Should().BeEmpty();
        fakes.Publisher.Published.Should().BeEmpty();
        // No se archiva si no se pudo tratar → se reintenta el próximo ciclo.
        fakes.Reader.Archived.Should().BeEmpty();
    }

    // ─── helpers ───

    private static EntidadExterna Correlacionada(
        Guid id, TipoEntidad tipo, long awDocId, int correlatedHoursAgo = 1)
    {
        var entidad = new EntidadExterna(
            id: id,
            tipoEntidad: tipo,
            referenciaExterna: "Q-" + awDocId,
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: "EDI",
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow.AddHours(-correlatedHoursAgo - 1),
            sucursal: "CIR");
        entidad.MarcarCorrelacionadaDirectamente(
            awDocId: awDocId,
            correlatedAt: DateTimeOffset.UtcNow.AddHours(-correlatedHoursAgo));
        return entidad;
    }

    private static AwDocumentsPage OnePage(params AwDocumentItem[] docs) =>
        new(docs, NextSince: null);

    private static DateTimeOffset NowMinutes(int delta) =>
        DateTimeOffset.UtcNow.AddMinutes(delta);

    private static (AwDocumentSyncWorker Worker, TestFakes Fakes) Build(
        Action<IntegracionesAwDbContext> seed)
    {
        var dbName = "aw-docsync-" + Guid.NewGuid().ToString("N");

        var services = new ServiceCollection();
        services.AddDbContext<IntegracionesAwDbContext>(opts => opts
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning)));

        var reader = new FakeDocumentsReader();
        var blob = new FakeBlob();
        var publisher = new FakePublisher();
        var notifier = new FakeNotifier();
        var agentRealtime = new FakeAgentRealtime();
        var agentWebhook = new FakeAgentWebhook();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var empresaContext = new FakeEmpresaContext();

        services.AddSingleton<IAwDocumentsReader>(reader);
        services.AddSingleton<IAlmacenarBlobPort>(blob);
        services.AddSingleton<IIntegrationEventPublisher>(publisher);
        services.AddSingleton<IIntegracionesAwNotifier>(notifier);
        services.AddSingleton<IAgentRealtimePublisher>(agentRealtime);
        services.AddSingleton<IAgentDocumentoWebhook>(agentWebhook);
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

        var worker = new AwDocumentSyncWorker(
            scopeFactory,
            Options.Create(new IntegracionesAwOptions()),
            new DocumentSyncKick(),
            NullLogger<AwDocumentSyncWorker>.Instance);

        Func<IntegracionesAwDbContext> fetchDb = () =>
            scopeFactory.CreateScope().ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();

        return (worker, new TestFakes(fetchDb, reader, blob, publisher, notifier, agentRealtime, agentWebhook));
    }

    private sealed record TestFakes(
        Func<IntegracionesAwDbContext> FetchDb,
        FakeDocumentsReader Reader,
        FakeBlob Blob,
        FakePublisher Publisher,
        FakeNotifier Notifier,
        FakeAgentRealtime AgentRealtime,
        FakeAgentWebhook AgentWebhook);

    // ─── fakes ───

    private sealed class FakeDocumentsReader : IAwDocumentsReader
    {
        public int ListCalls;
        public List<string> Downloaded { get; } = new();
        public List<string> Archived { get; } = new();
        public AwDocumentsPage NextPage { get; set; } =
            new(Array.Empty<AwDocumentItem>(), NextSince: null);
        public Exception? ThrowOnDownload { get; set; }

        public Task<AwDocumentsPage> ListSinceAsync(DateTimeOffset? since, int limit, CancellationToken ct)
        {
            ListCalls++;
            return Task.FromResult(NextPage);
        }

        public Task<Stream> DownloadAsync(string filename, CancellationToken ct)
        {
            if (ThrowOnDownload is not null) throw ThrowOnDownload;
            Downloaded.Add(filename);
            return Task.FromResult<Stream>(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4")));
        }

        public Task ArchivarAsync(string filename, CancellationToken ct)
        {
            Archived.Add(filename);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBlob : IAlmacenarBlobPort
    {
        public List<(Guid Id, string Filename)> Uploads { get; } = new();

        public Task<string> SubirAsync(Guid blobId, Stream contenido, string contentType,
            string nombreArchivoOriginal, CancellationToken ct)
        {
            Uploads.Add((blobId, nombreArchivoOriginal));
            return Task.FromResult($"file://blobs/{blobId:D}.pdf");
        }

        public Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task EliminarAsync(string blobUrl, CancellationToken ct) => Task.CompletedTask;
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
        public int DocumentoAdjuntadoCount;

        public Task NotifyCorrelacionExitosaAsync(Guid e, Guid a, string q, long d, DateTimeOffset w, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyCorrelacionExpiradaAsync(Guid e, Guid a, string q, DateTimeOffset s, DateTimeOffset x, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDropFallidoAsync(Guid e, Guid a, string q, string m, string k, int r, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDropDeadLetterAsync(Guid e, Guid a, string q, string m, string k, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyCotizacionEstadoActualizadoAsync(Guid e, Guid a, string q, string s, DateTimeOffset o, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDocumentoAdjuntadoAsync(Guid e, Guid a, string q, long d, string t, string f, DateTimeOffset u, CancellationToken ct)
        { DocumentoAdjuntadoCount++; return Task.CompletedTask; }
    }

    private sealed class FakeAgentRealtime : IAgentRealtimePublisher
    {
        public int Calls;
        public Task PublishCotizacionActualizadaAsync(EntidadExterna entidad, CancellationToken ct)
        { Calls++; return Task.CompletedTask; }
    }

    private sealed class FakeAgentWebhook : IAgentDocumentoWebhook
    {
        public int Calls;
        public Task NotifyPdfListoAsync(EntidadExterna entidad, CancellationToken ct)
        { Calls++; return Task.CompletedTask; }
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
