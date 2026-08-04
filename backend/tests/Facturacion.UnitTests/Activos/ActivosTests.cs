using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Activos.AutorizarVentaActivo;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Activos;

public sealed class ActivosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeActivos(ActivoFijoLectura? activo) : IActivosFijosReadPort
    {
        public Task<ActivoFijoLectura?> ObtenerAsync(string r, CancellationToken ct) => Task.FromResult(activo);
    }

    // ---- Dominio ----

    [Fact]
    public void Crear_calcula_valor_neto_y_utilidad()
    {
        var a = AutorizacionVentaActivo.Crear(Guid.NewGuid(), "AF-1", "Maquinaria", 100000m, 70000m, false, 50000m, Guid.NewGuid(), Ahora);

        a.Estado.Should().Be(EstadoAutorizacionActivo.Autorizada);
        a.ValorNetoEnLibros.Should().Be(30000m);
        a.UtilidadOPerdida.Should().Be(20000m); // 50000 - 30000
    }

    [Fact]
    public void MarcarUsada_no_es_reutilizable()
    {
        var a = AutorizacionVentaActivo.Crear(Guid.NewGuid(), "AF-1", "Maq", 100000m, 70000m, false, 50000m, Guid.NewGuid(), Ahora);
        a.MarcarUsada(Guid.NewGuid());

        a.Estado.Should().Be(EstadoAutorizacionActivo.Usada);
        var act = () => a.MarcarUsada(Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_NO_DISPONIBLE");
    }

    // ---- Handler autorizar ----

    private static AutorizarVentaActivoHandler AutorizarHandler(FacturacionDbContext db, Guid empresaId, ActivoFijoLectura? activo) =>
        new(db, new FakeActivos(activo), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    [Fact]
    public async Task Autorizar_activo_existente_registra_autorizacion()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var activo = new ActivoFijoLectura("AF-1", "Torno CNC", 100000m, 60000m, false);

        var resp = await AutorizarHandler(db, empresaId, activo).Handle(new AutorizarVentaActivoCommand("AF-1", 50000m), CancellationToken.None);

        resp.ValorNetoEnLibros.Should().Be(40000m);
        resp.UtilidadOPerdida.Should().Be(10000m);
        (await db.AutorizacionesVentaActivo.SingleAsync()).Estado.Should().Be(EstadoAutorizacionActivo.Autorizada);
    }

    [Fact]
    public async Task Autorizar_activo_inexistente_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => AutorizarHandler(db, empresaId, activo: null).Handle(new AutorizarVentaActivoCommand("AF-X", 50000m), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "ACTIVO_NO_EXISTE");
    }

    // ---- Emisión con autorización ----

    private static EmitirFacturaVentaHandler EmitirHandler(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeSender(new ReservarFolioResponse("AF-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaVentaCommand Command(Guid? autorizacionId) => new(
        Guid.NewGuid(), "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "01", "MXN", null,
        (short)10, ComportamientoFiscal.VentaActivoFijo, null, null, false,
        [new EmitirFacturaVentaLinea(null, "01010101", "Torno CNC", "H87", 1m, 50000m, 0m, "02", 0.16m, null, null)],
        Anticipos: null, Cce: null, AutorizacionId: autorizacionId);

    private static async Task<Guid> SembrarAutorizacionAsync(FacturacionDbContext db, Guid empresaId)
    {
        var a = AutorizacionVentaActivo.Crear(empresaId, "AF-1", "Torno CNC", 100000m, 60000m, false, 50000m, Guid.NewGuid(), Ahora);
        db.AutorizacionesVentaActivo.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    }

    [Fact]
    public async Task Emitir_venta_activo_sin_autorizacion_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => EmitirHandler(db, empresaId).Handle(Command(autorizacionId: null), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_REQUERIDA");
    }

    [Fact]
    public async Task Emitir_venta_activo_con_autorizacion_timbra_y_la_consume()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var autorizacionId = await SembrarAutorizacionAsync(db, empresaId);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(autorizacionId), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        (await db.FacturasVenta.SingleAsync()).AutorizacionId.Should().Be(autorizacionId);
        (await db.AutorizacionesVentaActivo.SingleAsync()).Estado.Should().Be(EstadoAutorizacionActivo.Usada);
    }

    [Fact]
    public async Task Emitir_con_autorizacion_ya_usada_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var autorizacionId = await SembrarAutorizacionAsync(db, empresaId);
        await EmitirHandler(db, empresaId).Handle(Command(autorizacionId), CancellationToken.None); // primera consume

        var act = () => EmitirHandler(db, empresaId).Handle(Command(autorizacionId), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_NO_DISPONIBLE");
    }
}
