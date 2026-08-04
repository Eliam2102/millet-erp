using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Facturas;

public sealed class EmitirFacturaVentaHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static EmitirFacturaVentaCommand Command() => new(
        SucursalId: Guid.NewGuid(),
        ReceptorRfc: "XAXX010101000",
        ReceptorNombre: "Público en general",
        ReceptorRegimenFiscal: "616",
        ReceptorCodigoPostal: "97000",
        ReceptorUsoCfdi: "S01",
        ReceptorPais: "MEX",
        RfcEmisor: "AAA010101AAA",
        RegimenFiscalEmisor: "601",
        MetodoPago: "PUE",
        FormaPago: "01",
        Moneda: "MXN",
        TipoCambio: null,
        CanalVenta: (short)1,
        ComportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
        ObraId: null,
        ObraNombre: null,
        FacturaAgrupada: false,
        Lineas: [new EmitirFacturaVentaLinea(null, "01010101", "Producto", "H87", 2m, 100m, 0m, "02", 0.16m, null, null)]);

    private static EmitirFacturaVentaHandler Handler(
        FacturacionDbContext db,
        Guid empresaId,
        bool periodoAbierto = true,
        bool catalogosValidos = true,
        Millet.Integraciones.Fiscal.Domain.Ports.ICfdiTimbradoPort? fiscal = null) =>
        new(
            db,
            new FakeSender(new ReservarFolioResponse("FA-000001", 1, "")),
            new FakePeriodoContablePort(periodoAbierto),
            new FakeCatalogosSatReadPort(catalogosValidos),
            fiscal ?? new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(),
            new FakeEmpresaContext(empresaId),
            new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora));

    [Fact]
    public async Task Handle_emite_factura_timbrada_con_uuid_fake()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, empresaId).Handle(Command(), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrEmpty();
        resp.Folio.Should().Be("FA-000001");
        resp.Total.Should().Be(232m);

        var factura = await db.FacturasVenta.Include(f => f.Lineas).SingleAsync();
        factura.Estado.Should().Be(EstadoTimbrado.Timbrado);
        factura.Uuid.Should().Be(resp.Uuid);
        factura.CfdiArchivoId.Should().NotBeNull();
        factura.PeriodoAnio.Should().Be(2026);
        factura.PeriodoMes.Should().Be(5);
        factura.Lineas.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_con_periodo_cerrado_lanza_PERIODO_CERRADO()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => Handler(db, empresaId, periodoAbierto: false).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PERIODO_CERRADO");
    }

    [Fact]
    public async Task Handle_con_catalogo_invalido_lanza()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => Handler(db, empresaId, catalogosValidos: false).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    /// <summary>PAC que rechaza siempre (rechazo limpio, no ambiguo).</summary>
    private sealed class FakeFiscalRechaza : Millet.Integraciones.Fiscal.Domain.Ports.ICfdiTimbradoPort
    {
        public Task<Millet.Integraciones.Fiscal.Domain.Ports.TimbradoResultado> TimbrarAsync(
            Millet.Integraciones.Fiscal.Domain.Ports.CfdiEmision emision, CancellationToken ct) =>
            Task.FromResult(new Millet.Integraciones.Fiscal.Domain.Ports.TimbradoResultado(
                Millet.Integraciones.Fiscal.Domain.Ports.TimbradoEstado.Fallido,
                null, null, null, null, null, null, null, "CFDI40139", "rechazo CFDI40139"));
        public Task<Millet.Integraciones.Fiscal.Domain.Ports.CancelacionResultado> CancelarAsync(
            Millet.Integraciones.Fiscal.Domain.Ports.CancelacionSolicitud s, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<Millet.Integraciones.Fiscal.Domain.Ports.EstatusCfdiResultado> ConsultarEstatusAsync(
            Millet.Integraciones.Fiscal.Domain.Ports.EstatusCfdiSolicitud s, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Handle_con_timbre_fallido_toma_el_pedido_y_bloquea_segunda_emision()
    {
        // [Decisión 01-G] G5: 1 pedido → 1 documento → N intentos. El fallo
        // del PAC NO libera el pedido; el reintento/descarte van por la factura.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = Millet.Facturacion.Domain.Pedidos.PedidoFacturable.CrearManual(
            empresaId, "P-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, Guid.NewGuid());
        db.PedidosFacturables.Add(pedido);
        db.SaveChanges();

        var resp = await Handler(db, empresaId, fiscal: new FakeFiscalRechaza())
            .Handle(Command() with { PedidoFacturableId = pedido.Id }, CancellationToken.None);

        resp.Estado.Should().Be("TimbradoFallido");
        pedido.Estado.Should().Be(Millet.Facturacion.Domain.Pedidos.EstadoPedidoFacturable.Facturado);
        pedido.ComprobanteVigenteId.Should().Be(resp.Id);

        // G4: la llamada fallida al PAC quedó en la bitácora.
        var intento = await db.BitacorasIntentoTimbrado.SingleAsync(b => b.ComprobanteId == resp.Id);
        intento.IntentoNumero.Should().Be(1);
        intento.Resultado.Should().Be(ResultadoIntentoTimbrado.Fallido);
        intento.ErrorCodigo.Should().Be("CFDI40139");

        // El pedido tomado bloquea una segunda emisión (invariante 5).
        var act = () => Handler(db, empresaId)
            .Handle(Command() with { PedidoFacturableId = pedido.Id }, CancellationToken.None);
        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "PEDIDO_NO_FACTURABLE");
    }

    [Fact]
    public async Task Handle_sin_empresa_lanza_Forbidden()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var handler = new EmitirFacturaVentaHandler(
            db,
            new FakeSender(new ReservarFolioResponse("FA-000001", 1, "")),
            new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(),
            new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(),
            new FakeEmpresaContext(current: null),
            new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora));

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "EMPRESA_NO_SELECCIONADA");
    }
}
