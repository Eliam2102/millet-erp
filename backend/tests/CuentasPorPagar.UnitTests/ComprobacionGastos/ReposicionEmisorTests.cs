using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Reposiciones;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.UnitTests.ComprobacionGastos;

public sealed class ReposicionEmisorTests : IDisposable
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _sucursal = Guid.NewGuid();
    private readonly RecordingPublisher _events = new();
    private readonly CuentasPorPagarDbContext _db;
    private readonly ReposicionCajaChicaEmisor _emisor;

    public ReposicionEmisorTests()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"cxp-emisor-{Guid.NewGuid():N}").Options;
        _db = new CuentasPorPagarDbContext(options, new EmpresaContext(_empresa), new PeriodoAbiertoStub(), new PeriodoAbiertoStub());
        _emisor = new ReposicionCajaChicaEmisor(_db, _events);
    }

    public void Dispose() => _db.Dispose();

    private Domain.ComprobacionGastos.ComprobacionGastos Aplicada(decimal total, bool conReposicion = false)
    {
        var c = Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            _empresa, TipoComprobacionGastos.ReembolsoCajaChica, _sucursal, Guid.NewGuid(),
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), "MXN", null,
            DateTimeOffset.UtcNow, destinoReposicion: DestinoReposicionCaja.CuentaSucursal);
        c.AgregarLinea(Guid.NewGuid(), null, null, Guid.NewGuid(), null, DateTimeOffset.UtcNow,
            total, 0m, 0m, total, "MXN", null);
        c.EnviarARevision(DateTimeOffset.UtcNow);
        c.AprobarPasivo(Guid.NewGuid(), DateTimeOffset.UtcNow);
        if (conReposicion) c.AsignarReposicion(Guid.NewGuid());
        _db.ComprobacionesGastos.Add(c);
        return c;
    }

    [Fact]
    public async Task Solo_una_comprobacion_emite_una_reposicion_de_una_comprobacion()
    {
        var a = Aplicada(100m);
        var b = Aplicada(50m);
        await _db.SaveChangesAsync();

        var r = await _emisor.EmitirSiCorrespondeAsync(_empresa, _sucursal, DestinoReposicionCaja.CuentaSucursal,
            false, Guid.NewGuid(), DateTimeOffset.UtcNow, default, soloComprobacionId: a.Id);

        r.Should().NotBeNull();
        r!.NumeroComprobaciones.Should().Be(1);
        r.MontoTotal.Should().Be(100m);
        r.EsCorteManual.Should().BeFalse();
        a.ReposicionId.Should().Be(r.Id);
        b.ReposicionId.Should().BeNull();
        _events.Eventos.Should().HaveCount(1);
    }

    [Fact]
    public async Task Sin_minimo_se_emite_un_monto_pequeno()
    {
        var a = Aplicada(10m);
        await _db.SaveChangesAsync();

        var r = await _emisor.EmitirSiCorrespondeAsync(_empresa, _sucursal, DestinoReposicionCaja.CuentaSucursal,
            false, null, DateTimeOffset.UtcNow, default, soloComprobacionId: a.Id);

        r!.MontoTotal.Should().Be(10m);
    }

    [Fact]
    public async Task Corte_manual_cubre_todas_las_aplicadas_sin_reposicion()
    {
        var a = Aplicada(100m);
        var b = Aplicada(50m);
        await _db.SaveChangesAsync();

        var r = await _emisor.EmitirSiCorrespondeAsync(_empresa, _sucursal, DestinoReposicionCaja.CuentaSucursal,
            true, null, DateTimeOffset.UtcNow, default);

        r!.NumeroComprobaciones.Should().Be(2);
        r.MontoTotal.Should().Be(150m);
        r.EsCorteManual.Should().BeTrue();
        a.ReposicionId.Should().Be(r.Id);
        b.ReposicionId.Should().Be(r.Id);
    }

    [Fact]
    public async Task Comprobacion_con_reposicion_previa_no_emite()
    {
        var a = Aplicada(100m, conReposicion: true);
        await _db.SaveChangesAsync();

        var r = await _emisor.EmitirSiCorrespondeAsync(_empresa, _sucursal, DestinoReposicionCaja.CuentaSucursal,
            false, null, DateTimeOffset.UtcNow, default, soloComprobacionId: a.Id);

        r.Should().BeNull();
        _events.Eventos.Should().BeEmpty();
    }

    private sealed class EmpresaContext(Guid id) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = id;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
