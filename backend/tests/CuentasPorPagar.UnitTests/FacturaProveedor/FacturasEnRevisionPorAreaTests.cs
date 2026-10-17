using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class FacturasEnRevisionPorAreaTests : IDisposable
{
    private static readonly Guid Danos = Guid.Parse("00000007-1001-0000-0000-000000000002");
    private static readonly Guid IndicacionExpresa = Guid.Parse("00000007-1001-0000-0000-00000000000d");
    private static readonly DateTimeOffset Entrada = DateTimeOffset.Parse("2026-10-16T21:00:00Z");
    private static readonly Guid Dependencia = Guid.NewGuid();

    private readonly CuentasPorPagarDbContext _db;
    private readonly RelojFijo _reloj = new(DateTimeOffset.Parse("2026-10-21T18:00:00Z"));

    public FacturasEnRevisionPorAreaTests()
    {
        _db = new CuentasPorPagarDbContext(
            new DbContextOptionsBuilder<CuentasPorPagarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new BypassedEmpresaContext(), new PeriodoAbiertoStub(), new PeriodoAbiertoStub());
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Devuelve_dias_habiles_y_plazo_del_motivo()
    {
        await SembrarAsync(Danos, Dependencia);

        var r = await Handle();

        var item = r.Items.Should().ContainSingle().Subject;
        item.DiasEnRevision.Should().Be(3);
        item.SlaDias.Should().Be(5);
        item.NivelAviso.Should().BeNull();
    }

    [Fact]
    public async Task NivelAviso_solo_cuenta_el_episodio_vigente()
    {
        var f = await SembrarAsync(Danos, Dependencia);
        _db.AvisosSlaRevision.Add(new AvisoSlaRevision(f.EmpresaId, f.Id, Dependencia, Danos,
            NivelEscalamientoSla.SlaVencido, new DateOnly(2026, 10, 23), 5, 5, Entrada, Entrada));
        _db.AvisosSlaRevision.Add(new AvisoSlaRevision(f.EmpresaId, f.Id, Dependencia, Danos,
            NivelEscalamientoSla.EscalamientoDirector, new DateOnly(2026, 9, 30), 10, 5, Entrada.AddDays(-30), Entrada));
        await _db.SaveChangesAsync();

        var r = await Handle();

        r.Items.Single().NivelAviso.Should().Be(2);
    }

    [Fact]
    public async Task Indicacion_expresa_no_tiene_plazo()
    {
        await SembrarAsync(IndicacionExpresa, Dependencia);

        (await Handle()).Items.Single().SlaDias.Should().BeNull();
    }

    [Fact]
    public async Task Otra_dependencia_no_aparece()
    {
        await SembrarAsync(Danos, Guid.NewGuid());

        (await Handle()).Items.Should().BeEmpty();
    }

    private Task<Millet.CuentasPorPagar.Application.Common.PagedResponse<FacturaEnRevisionResponse>> Handle() =>
        new FacturasEnRevisionPorAreaHandler(_db, _reloj, new Calendario())
            .Handle(new FacturasEnRevisionPorAreaQuery(Dependencia) { SucursalesPermitidas = null }, default);

    private async Task<Domain.FacturaProveedor.FacturaProveedor> SembrarAsync(Guid motivoId, Guid dependenciaId)
    {
        var f = Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(), sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: Entrada, fechaContabilizacion: Entrada,
            fechaVencimiento: new DateOnly(2026, 11, 30),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m, impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(), encargadoComprasSnapshot: null,
            tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m, redondeoAplicado: 0m, ahora: Entrada);
        f.EnviarARevision(motivoId, dependenciaId, null, Entrada);
        _db.FacturasProveedor.Add(f);
        await _db.SaveChangesAsync();
        return f;
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow { get; } = ahora;
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
