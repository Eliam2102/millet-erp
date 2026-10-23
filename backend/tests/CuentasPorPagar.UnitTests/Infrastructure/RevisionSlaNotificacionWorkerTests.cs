using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Workers;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;

namespace Millet.CuentasPorPagar.UnitTests.Infrastructure;

public sealed class RevisionSlaNotificacionWorkerTests : IDisposable
{
    private static readonly Guid Danos = Guid.Parse("00000007-1001-0000-0000-000000000002");
    private static readonly Guid Disputa = Guid.Parse("00000007-1001-0000-0000-00000000000b");
    private static readonly Guid IndicacionExpresa = Guid.Parse("00000007-1001-0000-0000-00000000000d");
    private static readonly DateTimeOffset Entrada = DateTimeOffset.Parse("2026-10-16T21:00:00Z");

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly RelojMutable _reloj = new();
    private readonly List<SlaRevisionNotificacion> _enviadas = [];
    private readonly ServiceProvider _sp;
    private readonly RevisionSlaNotificacionWorker _worker;

    public RevisionSlaNotificacionWorkerTests()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(_dbName).Options;
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext, BypassedEmpresaContext>();
        services.AddSingleton<IClock>(_reloj);
        services.AddSingleton<IAuditOriginContext, Millet.SharedKernel.Infrastructure.AuditOriginContext>();
        services.AddSingleton<INotificacionService>(new NotificacionesFake(_enviadas));
        services.AddSingleton<ICalendarioHabil, Calendario>();
        services.AddScoped(sp => new CuentasPorPagarDbContext(
            options, sp.GetRequiredService<ICurrentEmpresaContext>(), new PeriodoAbiertoStub(), sp.GetRequiredService<IClock>()));
        services.AddOptions<RevisionSlaNotificacionOptions>();
        services.AddLogging();
        _sp = services.BuildServiceProvider();
        _worker = new RevisionSlaNotificacionWorker(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<IOptionsMonitor<RevisionSlaNotificacionOptions>>(),
            NullLogger<RevisionSlaNotificacionWorker>.Instance);

        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().Database.EnsureCreated();
    }

    [Fact]
    public async Task A1a_motivo_de_5_dias_avisa_en_dias_habiles()
    {
        await SembrarAsync(Danos);

        await TickEnAsync("2026-10-18T18:00:00Z"); // domingo
        _enviadas.Should().BeEmpty();

        await TickEnAsync("2026-10-21T18:00:00Z"); // miércoles, 3 hábiles
        _enviadas.Should().ContainSingle().Which.Nivel.Should().Be(NivelEscalamientoSla.AvisoGerente);

        await TickEnAsync("2026-10-23T15:00:00Z"); // viernes, 5 hábiles
        var ultima = _enviadas[^1];
        ultima.Nivel.Should().Be(NivelEscalamientoSla.SlaVencido);
        ultima.DiasEnRevision.Should().Be(5);
    }

    [Fact]
    public async Task A1b_disputa_contractual_usa_umbrales_8_12_20()
    {
        await SembrarAsync(Disputa);

        await TickEnAsync("2026-10-27T18:00:00Z");
        _enviadas.Should().BeEmpty();

        await TickEnAsync("2026-10-28T18:00:00Z");
        _enviadas[^1].Nivel.Should().Be(NivelEscalamientoSla.AvisoGerente);
        await TickEnAsync("2026-11-03T18:00:00Z");
        _enviadas[^1].Nivel.Should().Be(NivelEscalamientoSla.SlaVencido);
        await TickEnAsync("2026-11-13T18:00:00Z");
        _enviadas[^1].Nivel.Should().Be(NivelEscalamientoSla.EscalamientoDirector);
    }

    [Fact]
    public async Task A2a_no_duplica_el_mismo_dia_y_si_el_dia_siguiente()
    {
        await SembrarAsync(Danos);

        await TickEnAsync("2026-10-23T15:00:00Z");
        await TickEnAsync("2026-10-23T15:00:00Z");
        _enviadas.Should().ContainSingle();
        (await ContarAvisosAsync()).Should().Be(1);

        await TickEnAsync("2026-10-26T15:00:00Z");
        (await ContarAvisosAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("00000007-1001-0000-0000-000000000002", true, "Daños o defectos en mercancía")]
    [InlineData("00000007-1001-0000-0000-00000000000b", false, "Disputa con proveedor")]
    [InlineData("00000007-1001-0000-0000-000000000005", true, "No conformidad con servicio o producto")]
    public async Task Aviso_incluye_a_Compras_si_el_motivo_lo_resuelve_Compras(string motivo, bool esperado, string nombre)
    {
        await SembrarAsync(Guid.Parse(motivo));

        await TickEnAsync("2026-11-13T18:00:00Z"); // 20 hábiles: nivel alcanzado en SLA 5 y 15

        var aviso = _enviadas.Should().ContainSingle().Subject;
        aviso.AvisaCompras.Should().Be(esperado);
        aviso.MotivoNombre.Should().Be(nombre);
        aviso.Folio.Should().Be("AF-001");
    }

    [Fact]
    public async Task Indicacion_expresa_no_avisa_y_sigue_en_revision()
    {
        var facturaId = await SembrarAsync(IndicacionExpresa);

        await TickEnAsync("2026-11-13T18:00:00Z");

        _enviadas.Should().BeEmpty();
        (await ContarAvisosAsync()).Should().Be(0);
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        (await db.FacturasProveedor.SingleAsync(f => f.Id == facturaId)).EnRevision.Should().BeTrue();
    }

    public void Dispose()
    {
        _worker.Dispose();
        _sp.Dispose();
    }

    private async Task TickEnAsync(string utc)
    {
        _reloj.UtcNow = DateTimeOffset.Parse(utc);
        await _worker.TickAsync(default);
    }

    private async Task<int> ContarAvisosAsync()
    {
        using var scope = _sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().AvisosSlaRevision.CountAsync();
    }

    private async Task<Guid> SembrarAsync(Guid motivoId)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var f = Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(), sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: Entrada, fechaContabilizacion: Entrada,
            fechaVencimiento: new DateOnly(2026, 11, 30),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m, impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(), encargadoComprasSnapshot: null,
            tolerancia: Domain.FacturaProveedor.Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m, redondeoAplicado: 0m, ahora: Entrada);
        f.EnviarARevision(motivoId, Guid.NewGuid(), null, Entrada);
        db.FacturasProveedor.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private sealed class RelojMutable : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class NotificacionesFake(List<SlaRevisionNotificacion> enviadas) : INotificacionService
    {
        public Task NotificarSlaRevisionAsync(SlaRevisionNotificacion notificacion, CancellationToken cancellationToken)
        {
            enviadas.Add(notificacion);
            return Task.CompletedTask;
        }
    }

    private sealed class Calendario : ICalendarioHabil
    {
        public Task<int> ContarDiasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct) =>
            Task.FromResult(CalendarioHabil.ContarDias(desde, hasta, CalendarioHabil.LeerFestivos(CalendarioHabil.FestivosIniciales)));

        public Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
