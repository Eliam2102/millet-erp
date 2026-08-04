using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Infrastructure.Workers;
using Millet.Integraciones.Fiscal.UnitTests.Application;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.UnitTests.Infrastructure;

public sealed class DescargaSubmitterWorkerTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Tick_sin_configuraciones_activas_no_hace_nada()
    {
        var (worker, _, sdk, sp, scope) = Build();
        try
        {
            await worker.TickAsync(CancellationToken.None);
            sdk.CrearSolicitudInvocaciones.Should().BeEmpty();
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_omite_rfc_sin_fiel_vigente()
    {
        var (worker, db, sdk, sp, scope) = Build();
        try
        {
            db.ConfiguracionesPac.Add(NewConfig());
            // RFC sin FIEL (FielValidFrom es null).
            db.RfcsReceptores.Add(new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA"));
            await db.SaveChangesAsync();

            await worker.TickAsync(CancellationToken.None);

            sdk.CrearSolicitudInvocaciones.Should().BeEmpty();
            sdk.AsegurarRuleInvocaciones.Should().BeEmpty();
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_omite_rfc_con_fiel_vencida()
    {
        var (worker, db, sdk, sp, scope) = Build();
        try
        {
            db.ConfiguracionesPac.Add(NewConfig());
            var rfc = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
            rfc.AsignarPersonExterno("p1");
            // FIEL vencida (validTo está en el pasado vs Ahora).
            rfc.AsignarFiel("c1", "k1",
                validFrom: Ahora.AddYears(-5),
                validTo:   Ahora.AddDays(-1),
                ahora:     Ahora.AddYears(-2));
            db.RfcsReceptores.Add(rfc);
            await db.SaveChangesAsync();

            await worker.TickAsync(CancellationToken.None);

            sdk.CrearSolicitudInvocaciones.Should().BeEmpty();
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_crea_rule_y_solicitudes_para_backfill_de_3_dias()
    {
        var (worker, db, sdk, sp, scope) = Build(backfillDays: 3, maxDaysPerRequest: 1);
        try
        {
            db.ConfiguracionesPac.Add(NewConfig());
            var rfc = NewRfcConFielVigente();
            db.RfcsReceptores.Add(rfc);
            await db.SaveChangesAsync();

            await worker.TickAsync(CancellationToken.None);

            sdk.AsegurarRuleInvocaciones.Should().HaveCount(1);
            sdk.CrearSolicitudInvocaciones.Should().HaveCount(3);
            db.DownloadRulesExternas.Should().HaveCount(1);
            db.SolicitudesDescarga.Should().HaveCount(3);
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    [Fact]
    public async Task Tick_es_idempotente_no_duplica_solicitudes_al_re_ejecutar()
    {
        var (worker, db, sdk, sp, scope) = Build(backfillDays: 3, maxDaysPerRequest: 1);
        try
        {
            db.ConfiguracionesPac.Add(NewConfig());
            db.RfcsReceptores.Add(NewRfcConFielVigente());
            await db.SaveChangesAsync();

            await worker.TickAsync(CancellationToken.None);
            await worker.TickAsync(CancellationToken.None);

            sdk.CrearSolicitudInvocaciones.Should().HaveCount(3); // Igual que un solo tick
            db.SolicitudesDescarga.Should().HaveCount(3);
        }
        finally { scope.Dispose(); sp.Dispose(); }
    }

    // ─────────────────────────── Helpers ────────────────────────────────

    private static ConfiguracionPac NewConfig() =>
        new(id:               Guid.NewGuid(),
            empresaId:        EmpresaId,
            proveedor:        ProveedorPac.FiscalApi,
            baseUrl:          "https://live.fiscalapi.com",
            apiKeyCifrado:    InMemoryFiscalDb.Cipher().Encrypt("k"),
            apiKeyHash:       FiscalSecretCipher.HashForChangeDetection("k"),
            ahora:            Ahora);

    private static RfcReceptor NewRfcConFielVigente()
    {
        var r = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
        r.AsignarPersonExterno("person-ext-1");
        r.AsignarFiel("cer-ext", "key-ext",
            validFrom: Ahora.AddYears(-1),
            validTo:   Ahora.AddYears(3),
            ahora:     Ahora);
        return r;
    }

    private static (DescargaSubmitterWorker Worker,
                    IntegracionesFiscalDbContext Db,
                    StubSdk Sdk,
                    ServiceProvider Sp,
                    IServiceScope Scope)
        Build(int backfillDays = 7, int maxDaysPerRequest = 1)
    {
        var dbName = $"submitter-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        var sdk = new StubSdk();
        services.AddSingleton<IFiscalApiSdkClient>(sdk);
        services.AddSingleton<IClock>(new InMemoryFiscalDb.FakeClock(Ahora));
        services.AddScoped<ICurrentEmpresaContext, InMemoryFiscalDb.BypassedEmpresaContext>();
        services.AddDbContext<IntegracionesFiscalDbContext>(o => o.UseInMemoryDatabase(dbName));

        var sp = services.BuildServiceProvider();
        var monitor = new TestOptionsMonitor(new IntegracionesFiscalWorkerOptions
        {
            Submitter = new IntegracionesFiscalWorkerOptions.SubmitterOptions
            {
                Disabled = false,
                TickIntervalSeconds = 3600,
                BackfillDays = backfillDays,
                MaxDaysPerRequest = maxDaysPerRequest,
            },
        });
        var worker = new DescargaSubmitterWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            NullLogger<DescargaSubmitterWorker>.Instance);

        var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();
        return (worker, db, sdk, sp, scope);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<IntegracionesFiscalWorkerOptions>
    {
        public TestOptionsMonitor(IntegracionesFiscalWorkerOptions value) { CurrentValue = value; }
        public IntegracionesFiscalWorkerOptions CurrentValue { get; }
        public IntegracionesFiscalWorkerOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<IntegracionesFiscalWorkerOptions, string?> listener) => null;
    }

    /// <summary>Stub mínimo de <see cref="IFiscalApiSdkClient"/> para tests.</summary>
    internal sealed class StubSdk : IFiscalApiSdkClient
    {
        public List<(Guid Empresa, string PersonId)> AsegurarRuleInvocaciones { get; } = new();
        public List<(Guid Empresa, string Rule, DateTimeOffset Inicio, DateTimeOffset Fin)> CrearSolicitudInvocaciones { get; } = new();
        public List<(Guid Empresa, string Request)> ConsultarSolicitudInvocaciones { get; } = new();
        public Func<Guid, string, DateTimeOffset, DateTimeOffset, SolicitudDescargaExternaDto>? CrearSolicitudFactory { get; set; }
        public Func<Guid, string, SolicitudDescargaExternaDto>? ConsultarSolicitudFactory { get; set; }
        public List<MetaItemDto> MetaItems { get; } = new();
        public List<XmlCfdiItemDto> XmlItems { get; } = new();
        private int _requestSeq;

        public Task<PersonExterno> AsegurarPersonAsync(Guid empresaId, string rfc, string legalName,
            string zipCode, string satTaxRegimeCode, string? satCfdiUseCode, string email,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task SincronizarPersonAsync(Guid empresaId, string personIdExterno, string legalName,
            string zipCode, string? satCfdiUseCode, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<TaxFilesSubidos> SubirTaxFilesAsync(Guid empresaId, string personIdExterno, string rfc,
            byte[] cerBytes, byte[] keyBytes, string password, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<DownloadRuleExternaDto> AsegurarDownloadRuleAsync(Guid empresaId, string personIdExterno,
            SatQueryType satQueryType, DownloadType downloadType, SatInvoiceStatusFilter satInvoiceStatus,
            string descripcion, CancellationToken cancellationToken)
        {
            AsegurarRuleInvocaciones.Add((empresaId, personIdExterno));
            return Task.FromResult(new DownloadRuleExternaDto(
                IdExterno: $"rule-ext-{Guid.NewGuid():N}",
                Tin: "MIL010101AAA",
                SatQueryType: satQueryType,
                DownloadType: downloadType,
                SatInvoiceStatus: satInvoiceStatus,
                IsTest: false));
        }

        public Task<SolicitudDescargaExternaDto> CrearSolicitudAsync(Guid empresaId, string ruleIdExterno,
            DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken)
        {
            CrearSolicitudInvocaciones.Add((empresaId, ruleIdExterno, startDate, endDate));
            if (CrearSolicitudFactory is not null)
                return Task.FromResult(CrearSolicitudFactory(empresaId, ruleIdExterno, startDate, endDate));
            _requestSeq++;
            return Task.FromResult(new SolicitudDescargaExternaDto(
                IdExterno: $"req-ext-{_requestSeq}",
                SatRequestStatusId: 0,
                DownloadRequestStatusId: 1,
                InvoiceCount: null,
                LastAttemptDate: null,
                NextAttemptDate: null,
                CreatedAt: DateTimeOffset.UtcNow));
        }

        public Task<SolicitudDescargaExternaDto> ConsultarSolicitudAsync(Guid empresaId,
            string requestIdExterno, CancellationToken cancellationToken)
        {
            ConsultarSolicitudInvocaciones.Add((empresaId, requestIdExterno));
            if (ConsultarSolicitudFactory is not null)
                return Task.FromResult(ConsultarSolicitudFactory(empresaId, requestIdExterno));
            return Task.FromResult(new SolicitudDescargaExternaDto(
                IdExterno: requestIdExterno,
                SatRequestStatusId: 3,
                DownloadRequestStatusId: 3,
                InvoiceCount: 0,
                LastAttemptDate: null,
                NextAttemptDate: null,
                CreatedAt: DateTimeOffset.UtcNow));
        }

        public async IAsyncEnumerable<MetaItemDto> ListarMetaItemsAsync(Guid empresaId,
            string requestIdExterno, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in MetaItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<XmlCfdiItemDto> ListarXmlsAsync(Guid empresaId,
            string requestIdExterno, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var xml in XmlItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return xml;
            }
            await Task.CompletedTask;
        }

        public Task<EstatusUuidDto> ConsultarEstatusUuidAsync(Guid empresaId, string uuidCfdi, string rfcEmisor,
            string rfcReceptor, decimal total, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<PingResultDto> PingAsync(Guid empresaId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
