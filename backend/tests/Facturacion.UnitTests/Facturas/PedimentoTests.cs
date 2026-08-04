using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas.AplicarPedimento;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Facturas;

public sealed class PedimentoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    // ---- Dominio ----

    private static FacturaVenta FacturaConPedimento(bool requiere)
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(Guid.NewGuid(), "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Producto importado", "H87", 1m, 100m, 0m, "02", 0.16m, null, null, requierePedimento: requiere);
        fv.RecalcularTotales();
        return fv;
    }

    [Fact]
    public void RequierePedimento_es_true_si_alguna_linea_lo_marca()
    {
        FacturaConPedimento(requiere: true).RequierePedimento.Should().BeTrue();
        FacturaConPedimento(requiere: false).RequierePedimento.Should().BeFalse();
    }

    [Fact]
    public void MarcarPendientePedimento_y_AplicarPedimento_completan_el_ciclo()
    {
        var fv = FacturaConPedimento(requiere: true);

        fv.MarcarPendientePedimento();
        fv.Estado.Should().Be(EstadoTimbrado.PendientePedimento);
        fv.PedimentoCompleto.Should().BeFalse();

        fv.AplicarPedimento("15  47  3001  0001234", new DateOnly(2026, 5, 20), "ID-1");

        fv.Estado.Should().Be(EstadoTimbrado.Borrador);
        fv.PedimentoCompleto.Should().BeTrue();
        fv.Lineas.Single().Pedimento.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MarcarPendientePedimento_sin_lineas_que_lo_requieran_lanza()
    {
        var fv = FacturaConPedimento(requiere: false);
        var act = () => fv.MarcarPendientePedimento();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_REQUIERE_PEDIMENTO");
    }

    // ---- Handler de emisión: compuerta ----

    private static EmitirFacturaVentaHandler EmitirHandler(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeSender(new ReservarFolioResponse("F-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    private static EmitirFacturaVentaCommand Command(bool requierePedimento) => new(
        Guid.NewGuid(), "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "01", "MXN", null,
        (short)1, ComportamientoFiscal.MostradorInmediato, null, null, false,
        [new EmitirFacturaVentaLinea(null, "01010101", "Producto", "H87", 1m, 100m, 0m, "02", 0.16m, null, null, requierePedimento)]);

    [Fact]
    public async Task Emitir_sin_requiere_pedimento_timbra_directo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(requierePedimento: false), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Emitir_con_requiere_pedimento_retiene_la_factura()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await EmitirHandler(db, empresaId).Handle(Command(requierePedimento: true), CancellationToken.None);

        resp.Estado.Should().Be("PendientePedimento");
        resp.Uuid.Should().BeNull(); // no se timbró
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.PendientePedimento);
    }

    // ---- Handler AplicarPedimento ----

    [Fact]
    public async Task AplicarPedimento_timbra_la_factura_retenida()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await EmitirHandler(db, empresaId).Handle(Command(requierePedimento: true), CancellationToken.None);
        var factura = await db.FacturasVenta.SingleAsync();

        var handler = new AplicarPedimentoHandler(db, new FakeSender(new ReservarFolioResponse("NC-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(), new FakeClock(Ahora));
        var resp = await handler.Handle(
            new AplicarPedimentoCommand(factura.Id, "15  47  3001  0001234", new DateOnly(2026, 5, 20), "ID-1"),
            CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrEmpty();
        var fv = await db.FacturasVenta.Include(f => f.Lineas).SingleAsync();
        fv.Lineas.Single().Pedimento.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AplicarPedimento_sobre_factura_no_retenida_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        await EmitirHandler(db, empresaId).Handle(Command(requierePedimento: false), CancellationToken.None);
        var factura = await db.FacturasVenta.SingleAsync(); // Timbrada, no PendientePedimento

        var handler = new AplicarPedimentoHandler(db, new FakeSender(new ReservarFolioResponse("NC-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(), new FakeClock(Ahora));
        var act = () => handler.Handle(new AplicarPedimentoCommand(factura.Id, "PED-1", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_PENDIENTE_PEDIMENTO");
    }
}
