using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// F6.6 (R16 · CA6.8): Caja solo aplica un cobro con forma 03 si trae el folio
/// de confirmación de Ingresos, vigente, del cliente del comprobante y por el
/// importe exacto (F6.6-a usado, F6.6-b importe distinto). Aplica también a la
/// liquidación de ruta (mismo registrador).
/// </summary>
public sealed class FolioTransferenciaCobroTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Criterio_2_forma_03_sin_folio_se_rechaza()
    {
        using var db = NewDb();
        var factura = await SembrarAsync(db, 8_000m);

        var act = () => Registrar(db, Port(), factura.Id, new CobroFormaPagoInput("03", 8_000m, "SPEI-1"));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("COBRO_TRANSFERENCIA_SIN_FOLIO");
        (await db.CobrosMostrador.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Criterio_3_con_folio_vigente_registra_el_cobro_y_publica_el_folio()
    {
        using var db = NewDb();
        var factura = await SembrarAsync(db, 8_000m);
        var eventos = new FakeIntegrationEventPublisher();

        await Registrar(db, Port(FakeFolioTransferenciaReadPort.Vigente("TR-000001", Cliente, 8_000m)), factura.Id,
            [new CobroFormaPagoInput("03", 8_000m, "SPEI-1", FolioTransferencia: "tr-000001")], eventos);

        var cobro = await db.CobrosMostrador.Include(c => c.FormasPago).SingleAsync();
        cobro.FormasPago.Single().FolioTransferencia.Should().Be("TR-000001");
        eventos.Publicados.OfType<CobroMostradorRegistradoIntegrationEvent>().Single()
            .FormasPago.Single().FolioTransferencia.Should().Be("TR-000001");
    }

    [Fact]
    public async Task Criterio_4_folio_usado_se_rechaza_en_Tesoreria_y_en_otro_cobro_activo()
    {
        using var db = NewDb();
        var primera = await SembrarAsync(db, 8_000m, "F-1");
        var segunda = await SembrarAsync(db, 8_000m, "F-2", sesion: false);
        var vigente = FakeFolioTransferenciaReadPort.Vigente("TR-000001", Cliente, 8_000m);

        var usadoEnTesoreria = () => Registrar(db, Port(vigente with { Estado = "Usado" }), primera.Id,
            new CobroFormaPagoInput("03", 8_000m, FolioTransferencia: "TR-000001"));
        (await usadoEnTesoreria.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("FOLIO_TR_NO_VIGENTE");

        // Tesorería aún no recibe el evento: el cobro activo que ya lo guardó lo bloquea.
        await Registrar(db, Port(vigente), primera.Id, new CobroFormaPagoInput("03", 8_000m, FolioTransferencia: "TR-000001"));
        var otraVez = () => Registrar(db, Port(vigente), segunda.Id,
            new CobroFormaPagoInput("03", 8_000m, FolioTransferencia: "TR-000001"));
        (await otraVez.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("FOLIO_TR_NO_VIGENTE");
    }

    [Fact]
    public async Task Criterio_5_folio_por_importe_distinto_se_rechaza()
    {
        using var db = NewDb();
        var factura = await SembrarAsync(db, 7_500m);

        var act = () => Registrar(db, Port(FakeFolioTransferenciaReadPort.Vigente("TR-000001", Cliente, 8_000m)), factura.Id,
            new CobroFormaPagoInput("03", 7_500m, FolioTransferencia: "TR-000001"));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("FOLIO_TR_IMPORTE_DISTINTO");
    }

    [Fact]
    public async Task Folio_de_otro_cliente_inexistente_o_repetido_se_rechaza()
    {
        using var db = NewDb();
        var factura = await SembrarAsync(db, 8_000m);

        var otroCliente = () => Registrar(db, Port(FakeFolioTransferenciaReadPort.Vigente("TR-000001", Guid.NewGuid(), 8_000m)),
            factura.Id, new CobroFormaPagoInput("03", 8_000m, FolioTransferencia: "TR-000001"));
        (await otroCliente.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("FOLIO_TR_OTRO_CLIENTE");

        var inexistente = () => Registrar(db, Port(), factura.Id,
            new CobroFormaPagoInput("03", 8_000m, FolioTransferencia: "TR-999999"));
        (await inexistente.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("FOLIO_TR_NO_ENCONTRADO");

        var repetido = () => Registrar(db, Port(FakeFolioTransferenciaReadPort.Vigente("TR-000001", Cliente, 4_000m)), factura.Id,
            new CobroFormaPagoInput("03", 4_000m, FolioTransferencia: "TR-000001"),
            new CobroFormaPagoInput("03", 4_000m, FolioTransferencia: "tr-000001"));
        (await repetido.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("COBRO_FOLIO_TRANSFERENCIA_REPETIDO");
    }

    [Fact]
    public async Task Cobro_mixto_solo_la_forma_03_lleva_folio_y_el_efectivo_no_lo_pide()
    {
        using var db = NewDb();
        var factura = await SembrarAsync(db, 8_000m);

        await Registrar(db, Port(FakeFolioTransferenciaReadPort.Vigente("TR-000002", Cliente, 5_000m)), factura.Id,
            new CobroFormaPagoInput("01", 3_000m, FolioTransferencia: "TR-IGNORADO"),
            new CobroFormaPagoInput("03", 5_000m, FolioTransferencia: "TR-000002"));

        var formas = (await db.CobrosMostrador.Include(c => c.FormasPago).SingleAsync()).FormasPago;
        formas.Single(f => f.FormaPago == "01").FolioTransferencia.Should().BeNull();
        formas.Single(f => f.FormaPago == "03").FolioTransferencia.Should().Be("TR-000002");
    }

    [Fact]
    public async Task Liquidacion_de_ruta_tambien_exige_folio_en_forma_03()
    {
        using var db = NewDb();
        var factura = await SembrarAsync(db, 8_000m);
        var handler = new LiquidarRutaHandler(db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
            new FakeSucursalesReadPort(), new FakeAlcanceCajaEvaluator(), new FakeIntegrationEventPublisher(),
            new FakeCatalogosSatReadPort(), Port());

        var act = () => handler.Handle(new LiquidarRutaCommand(
            [new LiquidacionRutaCobroInput(factura.Id, [new CobroFormaPagoInput("03", 8_000m)])]), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("COBRO_TRANSFERENCIA_SIN_FOLIO");
    }

    [Fact]
    public async Task Selector_de_caja_devuelve_los_folios_vigentes_del_cliente_del_comprobante()
    {
        using var db = NewDb();
        var factura = await SembrarAsync(db, 8_000m);
        var port = Port(
            FakeFolioTransferenciaReadPort.Vigente("TR-000001", Cliente, 8_000m),
            FakeFolioTransferenciaReadPort.Vigente("TR-000002", Guid.NewGuid(), 8_000m));

        var folios = await new FoliosTransferenciaCajaHandler(db, new FakeAlcanceCajaEvaluator(), port)
            .Handle(new FoliosTransferenciaCajaQuery(factura.Id), CancellationToken.None);

        folios.Should().ContainSingle().Which.Folio.Should().Be("TR-000001");
    }

    // ---- helpers ----

    private static FakeFolioTransferenciaReadPort Port(params FolioTransferenciaCajaDto[] folios) => new(folios);

    private static Task<CobroMostradorResponse> Registrar(
        FacturacionDbContext db, IFolioTransferenciaReadPort port, Guid comprobanteId, params CobroFormaPagoInput[] formas) =>
        Registrar(db, port, comprobanteId, formas, null);

    private static Task<CobroMostradorResponse> Registrar(
        FacturacionDbContext db, IFolioTransferenciaReadPort port, Guid comprobanteId,
        CobroFormaPagoInput[] formas, FakeIntegrationEventPublisher? eventos) =>
        new RegistrarCobroMostradorHandler(
                db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
                new FakeSucursalesReadPort(), new FakeAlcanceCajaEvaluator(),
                eventos ?? new FakeIntegrationEventPublisher(), new FakeCatalogosSatReadPort(), port)
            .Handle(new RegistrarCobroMostradorCommand(comprobanteId, formas), CancellationToken.None);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    /// <summary>Sesión abierta del cajero y una factura PUE timbrada con pedido del cliente.</summary>
    private static async Task<FacturaVenta> SembrarAsync(FacturacionDbContext db, decimal valor, string folio = "F-1", bool sesion = true)
    {
        if (sesion)
        {
            var caja = Caja.Crear(Empresa, "Caja Call Center", null);
            db.Cajas.Add(caja);
            db.CajaSesiones.Add(CajaSesion.Abrir(Empresa, caja.Id, SucursalId, Cajero, 500m,
                new DateOnly(2026, 10, 31), Ahora.AddHours(-2), null));
        }
        var pedido = PedidoFacturable.CrearManual(Empresa, $"P-{folio}", SucursalId, Cliente, "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, Cajero,
            ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio, null, null);
        db.PedidosFacturables.Add(pedido);
        var fv = FacturaVenta.CrearBorrador(Empresa, folio, 1, SucursalId, null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"),
            "PUE", "03", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "01", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }
}
