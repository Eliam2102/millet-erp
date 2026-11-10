using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// M-177 · la liquidación del chofer registra lo cobrado contra entrega en la caja de la sucursal: un cobro por albarán sobre su factura
/// timbrada (salida de PT con referencia = folio del albarán), en la sesión abierta de quien liquida y con origen «Liquidación de ruta».
/// Datos FIX.
/// </summary>
public sealed class CobrosRutaProductoTerminadoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 16, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    /// <summary>Pedido con una salida de PT (folio del albarán) y su factura timbrada por <paramref name="total"/>.</summary>
    private static async Task<FacturaVenta> FacturaDeAlbaranAsync(FacturacionDbContext db, string albaran, decimal total, bool timbrada = true)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(Empresa, $"AW-{albaran}", Guid.NewGuid(), Guid.NewGuid(), "Cliente FIX", (short)1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "42");
        pedido.AgregarLinea(null, "Vidrio", "30171500", "H87", 1m, total, 0m, false);
        pedido.RecalcularTotal();
        var salida = SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.ProductoTerminado, albaran, [(1, 1m)], Ahora);
        var fv = FacturaVenta.CrearBorrador(Empresa, $"F-{albaran}", 1, Sucursal, null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente FIX", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"),
            "PUE", "01", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "30171500", "Vidrio", "H87", 1m, total, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.LigarSalida(salida.Id, pedido.Id);
        if (timbrada)
        {
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        }
        db.PedidosFacturables.Add(pedido);
        db.SalidasFacturables.Add(salida);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    private static async Task<CajaSesion> SesionAsync(FacturacionDbContext db, Guid sucursal)
    {
        var caja = Caja.Crear(Empresa, "Caja FIX", null);
        var sesion = CajaSesion.Abrir(Empresa, caja.Id, sucursal, Cajero, 500m, new DateOnly(2026, 11, 11), Ahora.AddHours(-2), null);
        db.Cajas.Add(caja);
        db.CajaSesiones.Add(sesion);
        await db.SaveChangesAsync();
        return sesion;
    }

    private static RegistrarCobrosRutaPtHandler Handler(FacturacionDbContext db) =>
        new(db, new SenderCaja(db), new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeSucursalesReadPort());

    /// <summary>Envía <see cref="LiquidarRutaCommand"/> al handler real de la caja.</summary>
    private sealed class SenderCaja(FacturacionDbContext db) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            request is LiquidarRutaCommand liquidar
                ? (TResponse)(object)await new LiquidarRutaHandler(db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero),
                    new FakeClock(Ahora), new FakeSucursalesReadPort(), new FakeAlcanceCajaEvaluator(), new FakeIntegrationEventPublisher(),
                    new FakeCatalogosSatReadPort()).Handle(liquidar, cancellationToken)
                : throw new NotImplementedException(request.GetType().Name);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task CA1_efectivo_y_voucher_de_debito_entran_a_la_sesion_como_liquidacion_de_ruta()
    {
        using var db = NewDb();
        var sesion = await SesionAsync(db, Sucursal);
        var a = await FacturaDeAlbaranAsync(db, "ALB-0001", 3_480m);
        var b = await FacturaDeAlbaranAsync(db, "ALB-0002", 5_220m);

        var r = await Handler(db).Handle(new RegistrarCobrosRutaPtCommand(Sucursal, [
            new CobroRutaAlbaranPtInput("ALB-0001", 3_480m, 0m, null, null, null),
            new CobroRutaAlbaranPtInput("ALB-0002", 0m, 5_220m, "AUT4821", "Debito", "4821"),
        ]), default);

        r.Should().HaveCount(2).And.OnlyContain(x => !x.YaExistia);
        var cobros = await db.CobrosMostrador.Include(c => c.FormasPago).ToListAsync();
        cobros.Should().HaveCount(2).And.OnlyContain(c => c.CajaSesionId == sesion.Id && c.Origen == OrigenCobroMostrador.LiquidacionRuta);
        cobros.Single(c => c.ComprobanteId == a.Id).FormasPago.Should().ContainSingle(f => f.FormaPago == "01" && f.Importe == 3_480m);
        cobros.Single(c => c.ComprobanteId == b.Id).FormasPago.Should().ContainSingle(f => f.FormaPago == "28" && f.Importe == 5_220m
            && f.Referencia == "AUT4821" && f.TarjetaUltimos4 == "4821");
    }

    [Fact]
    public async Task CA4_una_factura_que_ya_tiene_cobro_de_caja_no_se_cobra_otra_vez()
    {
        using var db = NewDb();
        await SesionAsync(db, Sucursal);
        await FacturaDeAlbaranAsync(db, "ALB-0001", 3_480m);
        var cobro = new CobroRutaAlbaranPtInput("ALB-0001", 3_480m, 0m, null, null, null);
        var primero = await Handler(db).Handle(new RegistrarCobrosRutaPtCommand(Sucursal, [cobro]), default);

        var segundo = await Handler(db).Handle(new RegistrarCobrosRutaPtCommand(Sucursal, [cobro]), default);

        segundo.Should().ContainSingle().Which.Should().Be(new CobroRutaAlbaranPtResultado("ALB-0001", primero[0].CobroId, true));
        (await db.CobrosMostrador.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CA5_sin_sesion_abierta_en_la_sucursal_de_la_hoja_no_registra_nada()
    {
        using var db = NewDb();
        await SesionAsync(db, Guid.NewGuid());   // abierta, pero en otra sucursal
        await FacturaDeAlbaranAsync(db, "ALB-0001", 3_480m);

        var act = () => Handler(db).Handle(new RegistrarCobrosRutaPtCommand(Sucursal, [new CobroRutaAlbaranPtInput("ALB-0001", 3_480m, 0m, null, null, null)]), default);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        (ex.Code, ex.Message).Should().Be(("COBRO_SIN_SESION_SUCURSAL",
            "No tiene sesión de caja abierta en MID — Mérida; abra su sesión en esa sucursal para liquidar la ruta."));
        (await db.CobrosMostrador.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CA6_albaran_sin_factura_timbrada_da_error_con_su_folio_y_no_registra_ningun_cobro()
    {
        using var db = NewDb();
        await SesionAsync(db, Sucursal);
        await FacturaDeAlbaranAsync(db, "ALB-0001", 3_480m);
        await FacturaDeAlbaranAsync(db, "ALB-0002", 5_220m, timbrada: false);

        var act = () => Handler(db).Handle(new RegistrarCobrosRutaPtCommand(Sucursal, [
            new CobroRutaAlbaranPtInput("ALB-0001", 3_480m, 0m, null, null, null),
            new CobroRutaAlbaranPtInput("ALB-0002", 5_220m, 0m, null, null, null),
        ]), default);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        (ex.Code, ex.Message).Should().Be(("PT_ALBARAN_SIN_FACTURA", "El albarán ALB-0002 no tiene factura timbrada; factúralo antes de liquidar la ruta."));
        (await db.CobrosMostrador.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Lo_que_se_espera_cobrar_es_el_total_de_la_factura_del_albaran_y_si_ya_esta_cobrada()
    {
        using var db = NewDb();
        await SesionAsync(db, Sucursal);
        var a = await FacturaDeAlbaranAsync(db, "ALB-0001", 3_480m);
        await Handler(db).Handle(new RegistrarCobrosRutaPtCommand(Sucursal, [new CobroRutaAlbaranPtInput("ALB-0001", 3_480m, 0m, null, null, null)]), default);
        await FacturaDeAlbaranAsync(db, "ALB-0002", 5_220m);

        var r = await new FacturasAlbaranesPtHandler(db).Handle(new FacturasAlbaranesPtQuery(["ALB-0001", "ALB-0002", "ALB-9999"]), default);

        r.Select(x => (x.AlbaranFolio, x.Folio, x.PorCobrar, x.CobroVigenteId is not null)).Should().BeEquivalentTo(
            [("ALB-0001", "F-ALB-0001", 3_480m, true), ("ALB-0002", "F-ALB-0002", 5_220m, false)]);
        r.Single(x => x.AlbaranFolio == "ALB-0001").ComprobanteId.Should().Be(a.Id);
    }

    [Theory]
    [InlineData("Credito", "04")]
    [InlineData("Debito", "28")]
    public void El_voucher_se_registra_con_la_forma_de_su_tipo_de_tarjeta(string tipo, string forma) =>
        RegistrarCobrosRutaPtHandler.FormasPago(new CobroRutaAlbaranPtInput("ALB-1", 100m, 50m, "AUT1", tipo, "1234"))
            .Select(f => (f.FormaPago, f.Importe)).Should().Equal(("01", 100m), (forma, 50m));
}
