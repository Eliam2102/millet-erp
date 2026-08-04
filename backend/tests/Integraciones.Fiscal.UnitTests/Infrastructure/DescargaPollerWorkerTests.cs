using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Infrastructure.Workers;
using Millet.Integraciones.Fiscal.UnitTests.Application;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.UnitTests.Infrastructure;

public sealed class DescargaPollerWorkerTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Tick_sin_solicitudes_vivas_no_hace_nada()
    {
        var (worker, _, sdk, receiver, sp, scope) = Build();
        try
        {
            await worker.TickAsync(CancellationToken.None);
            sdk.ConsultarSolicitudInvocaciones.Should().BeEmpty();
            receiver.Entregados.Should().BeEmpty();
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_consulta_solicitud_esperando_sat_y_avanza_fsm()
    {
        var (worker, db, sdk, _, sp, scope) = Build();
        try
        {
            var (rfc, rule) = await SeedRfcYRule(db);
            var s = new SolicitudDescarga(
                Guid.NewGuid(), EmpresaId, rule.Id, "req-1",
                Ahora.AddDays(-1), Ahora, Ahora.AddMinutes(-5)); // next_poll vencido
            db.SolicitudesDescarga.Add(s);
            await db.SaveChangesAsync();

            sdk.ConsultarSolicitudFactory = (_, _) => new SolicitudDescargaExternaDto(
                IdExterno: "req-1",
                SatRequestStatusId: 2,            // En proceso
                DownloadRequestStatusId: 1,       // Esperando SAT
                InvoiceCount: null,
                LastAttemptDate: null, NextAttemptDate: null,
                CreatedAt: Ahora);

            await worker.TickAsync(CancellationToken.None);

            sdk.ConsultarSolicitudInvocaciones.Should().HaveCount(1);
            // El worker corre en otro scope/DbContext; limpiamos tracker
            // para que la query del test recargue del store, no del cache.
            db.ChangeTracker.Clear();
            var reloaded = db.SolicitudesDescarga.Single();
            reloaded.Estado.Should().Be(EstadoSolicitudDescarga.EsperandoSat);
            reloaded.AttemptsPoll.Should().Be(1);
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_completada_3_cosecha_y_marca_cosechada()
    {
        var (worker, db, sdk, receiver, sp, scope) = Build();
        try
        {
            var (rfc, rule) = await SeedRfcYRule(db);
            var s = new SolicitudDescarga(
                Guid.NewGuid(), EmpresaId, rule.Id, "req-1",
                Ahora.AddDays(-1), Ahora, Ahora.AddMinutes(-5));
            db.SolicitudesDescarga.Add(s);
            await db.SaveChangesAsync();

            sdk.ConsultarSolicitudFactory = (_, _) => new SolicitudDescargaExternaDto(
                "req-1", 3, 3, 2, null, null, Ahora);
            sdk.MetaItems.Add(new MetaItemDto(
                Uuid: "AAA-001", RfcEmisor: "EMI", NombreEmisor: "Emisor SA",
                RfcReceptor: rfc.Rfc, NombreReceptor: "Millet",
                FechaCfdi: Ahora, FechaCertificacionSat: Ahora,
                Total: 1160m, TipoComprobante: "I", EstatusSat: "Vigente",
                FechaCancelacion: null));
            sdk.MetaItems.Add(new MetaItemDto(
                "BBB-002", "EMI", "Emisor SA", rfc.Rfc, "Millet",
                Ahora, Ahora, 580m, "I", "Vigente", null));
            // PR-14: el Poller filtra meta-items sin XML pareable. Añadimos
            // los XMLs (stub bytes) para que se entreguen al receiver.
            sdk.XmlItems.Add(new XmlCfdiItemDto("AAA-001", new byte[] { 0xAA, 0x01 }));
            sdk.XmlItems.Add(new XmlCfdiItemDto("BBB-002", new byte[] { 0xBB, 0x02 }));

            await worker.TickAsync(CancellationToken.None);

            receiver.Entregados.Should().HaveCount(2);
            receiver.Entregados.Select(p => p.Uuid).Should().Contain(["AAA-001", "BBB-002"]);
            receiver.Entregados.All(p => p.EmpresaId == EmpresaId).Should().BeTrue();
            receiver.Entregados.All(p => p.RfcReceptorMillet == rfc.Rfc).Should().BeTrue();
            receiver.Entregados.All(p => p.XmlBytes.Length > 0).Should().BeTrue();

            db.ChangeTracker.Clear();
            var reloaded = db.SolicitudesDescarga.Single();
            reloaded.Estado.Should().Be(EstadoSolicitudDescarga.Cosechada);
            reloaded.CosechadaAt.Should().Be(Ahora);
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_sat_error_4_marca_solicitud_en_error_y_no_cosecha()
    {
        var (worker, db, sdk, receiver, sp, scope) = Build();
        try
        {
            var (_, rule) = await SeedRfcYRule(db);
            db.SolicitudesDescarga.Add(new SolicitudDescarga(
                Guid.NewGuid(), EmpresaId, rule.Id, "req-1",
                Ahora.AddDays(-1), Ahora, Ahora.AddMinutes(-5)));
            await db.SaveChangesAsync();

            sdk.ConsultarSolicitudFactory = (_, _) => new SolicitudDescargaExternaDto(
                "req-1", 4, 1, null, null, null, Ahora);

            await worker.TickAsync(CancellationToken.None);

            receiver.Entregados.Should().BeEmpty();
            db.ChangeTracker.Clear();
            db.SolicitudesDescarga.Single().Estado.Should().Be(EstadoSolicitudDescarga.Error);
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_ignora_solicitudes_con_next_poll_futuro()
    {
        var (worker, db, sdk, _, sp, scope) = Build();
        try
        {
            var (_, rule) = await SeedRfcYRule(db);
            db.SolicitudesDescarga.Add(new SolicitudDescarga(
                Guid.NewGuid(), EmpresaId, rule.Id, "req-1",
                Ahora.AddDays(-1), Ahora,
                ahora: Ahora.AddMinutes(60))); // next_poll en el futuro
            await db.SaveChangesAsync();

            await worker.TickAsync(CancellationToken.None);

            sdk.ConsultarSolicitudInvocaciones.Should().BeEmpty();
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_solicitud_ya_terminada_pero_sin_cosechar_solo_cosecha()
    {
        var (worker, db, sdk, receiver, sp, scope) = Build();
        try
        {
            var (rfc, rule) = await SeedRfcYRule(db);
            var s = new SolicitudDescarga(
                Guid.NewGuid(), EmpresaId, rule.Id, "req-1",
                Ahora.AddDays(-1), Ahora, Ahora.AddMinutes(-5));
            s.AplicarPoll(3, 3, 1, Ahora.AddMinutes(-3)); // ya está en Terminada
            db.SolicitudesDescarga.Add(s);
            await db.SaveChangesAsync();

            sdk.MetaItems.Add(new MetaItemDto(
                "AAA-001", "EMI", "Emisor", rfc.Rfc, "Millet",
                Ahora, Ahora, 100m, "I", "Vigente", null));
            sdk.XmlItems.Add(new XmlCfdiItemDto("AAA-001", new byte[] { 0xAA, 0x01 }));

            await worker.TickAsync(CancellationToken.None);

            // No re-consulta — ya está Terminada, solo cosecha.
            sdk.ConsultarSolicitudInvocaciones.Should().BeEmpty();
            receiver.Entregados.Should().HaveCount(1);
            db.ChangeTracker.Clear();
            db.SolicitudesDescarga.Single().Estado.Should().Be(EstadoSolicitudDescarga.Cosechada);
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    // ─────────────────────────── Helpers ────────────────────────────────

    private static async Task<(RfcReceptor Rfc, DownloadRuleExterna Rule)> SeedRfcYRule(IntegracionesFiscalDbContext db)
    {
        var rfc = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
        db.RfcsReceptores.Add(rfc);
        var rule = new DownloadRuleExterna(
            Guid.NewGuid(), EmpresaId, rfc.Id, "rule-ext-1",
            SatQueryType.Cfdi, DownloadType.Recibidos, SatInvoiceStatusFilter.Todos);
        db.DownloadRulesExternas.Add(rule);
        await db.SaveChangesAsync();
        return (rfc, rule);
    }

    private static (DescargaPollerWorker Worker,
                    IntegracionesFiscalDbContext Db,
                    DescargaSubmitterWorkerTests.StubSdk Sdk,
                    CapturingReceiver Receiver,
                    ServiceProvider Sp,
                    IServiceScope Scope)
        Build()
    {
        var dbName = $"poller-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        var sdk = new DescargaSubmitterWorkerTests.StubSdk();
        var receiver = new CapturingReceiver();
        services.AddSingleton<IFiscalApiSdkClient>(sdk);
        services.AddSingleton<IFiscalCfdiReceiver>(receiver);
        services.AddSingleton<IClock>(new InMemoryFiscalDb.FakeClock(Ahora));
        services.AddScoped<ICurrentEmpresaContext, InMemoryFiscalDb.BypassedEmpresaContext>();
        services.AddDbContext<IntegracionesFiscalDbContext>(o => o.UseInMemoryDatabase(dbName));

        var sp = services.BuildServiceProvider();
        var monitor = new TestOptionsMonitor(new IntegracionesFiscalWorkerOptions
        {
            Poller = new IntegracionesFiscalWorkerOptions.PollerOptions
            {
                Disabled = false, TickIntervalSeconds = 900,
                BatchSize = 50, CosechaChunkSize = 200, MaxBackoffMinutes = 360,
            },
        });
        var worker = new DescargaPollerWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            NullLogger<DescargaPollerWorker>.Instance);

        var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();
        return (worker, db, sdk, receiver, sp, scope);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<IntegracionesFiscalWorkerOptions>
    {
        public TestOptionsMonitor(IntegracionesFiscalWorkerOptions value) { CurrentValue = value; }
        public IntegracionesFiscalWorkerOptions CurrentValue { get; }
        public IntegracionesFiscalWorkerOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<IntegracionesFiscalWorkerOptions, string?> listener) => null;
    }

    internal sealed class CapturingReceiver : IFiscalCfdiReceiver
    {
        public List<CfdiCosechadoPayload> Entregados { get; } = new();
        public Task IngresarCfdiAsync(CfdiCosechadoPayload payload, CancellationToken cancellationToken)
        {
            Entregados.Add(payload);
            return Task.CompletedTask;
        }
    }
}
