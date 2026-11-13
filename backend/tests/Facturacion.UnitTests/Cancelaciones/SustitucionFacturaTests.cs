using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Cancelaciones.AutorizarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.RechazarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.RetirarCancelacion;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Sustituciones;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

/// <summary>
/// FAC-08b: «Sustituir» emite la factura B con relación 04 a A; al timbrar B el ERP solicita
/// la cancelación de A con motivo 01 y B como sustituto, por los dos pasos de F6.5.
/// </summary>
public sealed class SustitucionFacturaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Rfc = "AAA010101AAA";
    private const string UuidA = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid Ingresos = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeSenderFolios : ISender
    {
        private int _n;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is ReservarFolioCommand)
            {
                _n++;
                return Task.FromResult((TResponse)(object)new ReservarFolioResponse($"DOC-{_n:D6}", _n, ""));
            }
            throw new NotImplementedException();
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeFiscalUuids : ICfdiTimbradoPort
    {
        private int _n;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _n++;
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, $"00000000-0000-0000-0000-{_n:D12}", "S", "S", "0", Ahora, "SAT970701NN3",
                $"<cfdi serie=\"{emision.Serie}\" folio=\"{emision.Folio}\" fake=\"true\" />", null, null));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "Cancelado", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private static async Task<(FacturaVenta, PedidoFacturable)> SembrarOriginalAsync(FacturacionDbContext db, Guid empresaId, Guid? productoId = null)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        var receptor = new DatosFiscalesReceptor(Rfc, "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(productoId, "01010101", "P", "H87", productoId is null ? 1m : 1.125m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(UuidA, null, null, null, Ahora, null, null);
        pedido.MarcarFacturado(fv.Id);
        db.PedidosFacturables.Add(pedido);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return (fv, pedido);
    }

    private static EmitirFacturaVentaHandler Emisor(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeSenderFolios(), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), new FakeFiscalUuids(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId), new FakeUserContext(Ingresos, "Ingresos"), new FakeClock(Ahora),
            ReceptorFiscalTestFactory.Crear(db));

    private static EmitirFacturaVentaCommand Sustituta(Guid originalId, Guid? pedidoId = null) => new(
        Guid.NewGuid(), Rfc, "Cliente corregido", "601", "97000", "G03", "MEX", "BBB010101BBB", "601",
        "PUE", "03", "MXN", null, (short)1, ComportamientoFiscal.MostradorInmediato, null, null, false,
        [new EmitirFacturaVentaLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null)],
        PedidoFacturableId: pedidoId, SustituyeComprobanteId: originalId);

    private static AutorizarCancelacionHandler Autorizador(FacturacionDbContext db) =>
        new(db, new FakeFiscalUuids(), new FakeIntegrationEventPublisher(),
            new FakeUserContext(Contador, "Contador"), new FakeClock(Ahora));

    [Fact]
    public async Task Sustituir_timbra_B_con_relacion_04_y_solicita_cancelar_A_con_motivo_01_sin_capturar_UUID()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (a, _) = await SembrarOriginalAsync(db, empresaId);

        var resp = await Emisor(db, empresaId).Handle(Sustituta(a.Id), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        var b = await db.FacturasVenta.Include(f => f.Relaciones).SingleAsync(f => f.Id == resp.Id);
        b.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "04" && r.UuidRelacionado == UuidA);
        b.PedidoFacturableId.Should().BeNull("hereda el pedido hasta que se cancele la original");

        var solicitud = await db.SolicitudesCancelacion.SingleAsync();
        solicitud.ComprobanteId.Should().Be(a.Id);
        solicitud.MotivoSat.Should().Be("01");
        solicitud.UuidSustituto.Should().Be(b.Uuid);
        solicitud.Estado.Should().Be(EstadoSolicitudCancelacion.PendienteAutorizacion);
        solicitud.SolicitadaPorId.Should().Be(Ingresos);
        (await db.Comprobantes.SingleAsync(c => c.Id == a.Id)).Estado.Should().Be(EstadoTimbrado.Timbrado);
    }

    [Fact]
    public async Task Al_autorizar_la_original_se_cancela_y_su_pedido_pasa_a_la_sustituta()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (a, pedido) = await SembrarOriginalAsync(db, empresaId);
        var resp = await Emisor(db, empresaId).Handle(Sustituta(a.Id), CancellationToken.None);

        await Autorizador(db).Handle(new AutorizarCancelacionCommand(a.Id), CancellationToken.None);

        (await db.Comprobantes.SingleAsync(c => c.Id == a.Id)).Estado.Should().Be(EstadoTimbrado.Cancelado);
        var p = await db.PedidosFacturables.SingleAsync(x => x.Id == pedido.Id);
        p.Estado.Should().Be(EstadoPedidoFacturable.Facturado, "el pedido no queda libre para refacturarse");
        p.ComprobanteVigenteId.Should().Be(resp.Id);
        (await db.FacturasVenta.SingleAsync(f => f.Id == resp.Id)).PedidoFacturableId.Should().Be(pedido.Id);
    }

    [Fact]
    public async Task Una_factura_ya_sustituida_no_se_vuelve_a_sustituir()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (a, _) = await SembrarOriginalAsync(db, empresaId);
        await Emisor(db, empresaId).Handle(Sustituta(a.Id), CancellationToken.None);

        var act = () => Emisor(db, empresaId).Handle(Sustituta(a.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SUSTITUCION_YA_SUSTITUIDA");
    }

    [Fact]
    public async Task Una_factura_cancelada_no_se_puede_sustituir()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (a, _) = await SembrarOriginalAsync(db, empresaId);
        a.MarcarCancelacionPendiente();
        a.MarcarCancelado();
        await db.SaveChangesAsync();

        var act = () => Emisor(db, empresaId).Handle(Sustituta(a.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SUSTITUCION_NO_PERMITIDA");
    }

    [Fact]
    public async Task Sustituta_no_se_liga_a_pedido_propio()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (a, pedido) = await SembrarOriginalAsync(db, empresaId);

        var act = () => Emisor(db, empresaId).Handle(Sustituta(a.Id, pedido.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SUSTITUCION_COMBINACION_NO_SOPORTADA");
        (await db.SolicitudesCancelacion.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Rechazada_la_sustitucion_se_solicita_cancelar_la_sustituta_con_motivo_03()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (a, pedido) = await SembrarOriginalAsync(db, empresaId);
        var resp = await Emisor(db, empresaId).Handle(Sustituta(a.Id), CancellationToken.None);

        await new RechazarCancelacionHandler(db, new FakeUserContext(Contador, "Contador"), new FakeClock(Ahora))
            .Handle(new RechazarCancelacionCommand(a.Id, "El cliente pidió conservar la original"), CancellationToken.None);

        (await db.Comprobantes.SingleAsync(c => c.Id == a.Id)).Estado.Should().Be(EstadoTimbrado.Timbrado);
        var deB = await db.SolicitudesCancelacion.SingleAsync(s => s.ComprobanteId == resp.Id);
        deB.MotivoSat.Should().Be("03");
        deB.UuidSustituto.Should().BeNull();
        deB.Estado.Should().Be(EstadoSolicitudCancelacion.PendienteAutorizacion);
        deB.SolicitadaPorId.Should().Be(Ingresos, "el Contador General puede autorizarla: no es su propia solicitud");
        (await db.PedidosFacturables.SingleAsync(x => x.Id == pedido.Id)).ComprobanteVigenteId.Should().Be(a.Id);
    }

    [Fact]
    public async Task La_cancelacion_por_sustitucion_no_se_puede_retirar()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (a, _) = await SembrarOriginalAsync(db, empresaId);
        await Emisor(db, empresaId).Handle(Sustituta(a.Id), CancellationToken.None);

        var act = () => new RetirarCancelacionHandler(db, new FakeClock(Ahora))
            .Handle(new RetirarCancelacionCommand(a.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "SUSTITUCION_NO_RETIRABLE");
    }
    [Fact]
    public async Task FB48_Borrador_sustitucion_resuelve_precision_sin_modificar_original()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var productoId = Guid.NewGuid();
        var (original, _) = await SembrarOriginalAsync(db, empresa, productoId);
        var producto = new ProductoFiscalLectura(productoId, "Kg actual", "01010101", "H87", "02", 0.16m,
            null, null, "Aw", DecimalesCantidad: 3);
        var handler = new DatosSustitucionHandler(db, new FakeAlcanceCajaEvaluator(), new FakeClock(Ahora), new FakeProductosReadPort(producto));

        var borrador = await handler.Handle(new DatosSustitucionQuery(original.Id), CancellationToken.None);

        borrador.Lineas.Single(l => l.ProductoId == productoId).DecimalesCantidad.Should().Be(3);
        borrador.Lineas.Single(l => l.ProductoId == productoId).Cantidad.Should().Be(1.125m);
        original.Lineas.Single(l => l.ProductoId == productoId).Cantidad.Should().Be(1.125m);

    }

    [Fact]
    public async Task FB48_Sustitucion_fraccionaria_no_emite_ni_solicita_cancelacion()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (original, _) = await SembrarOriginalAsync(db, empresa);
        var command = Sustituta(original.Id);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Emisor(db, empresa)
            .Handle(command with { Lineas = [command.Lineas[0] with { Cantidad = 4.5m }] }, CancellationToken.None));
        error.Code.Should().Be("CANTIDAD_DECIMALES_EXCEDE_UNIDAD");
        db.FacturasVenta.Should().ContainSingle(f => f.Id == original.Id);
        db.SolicitudesCancelacion.Should().BeEmpty();
        original.Estado.Should().Be(EstadoTimbrado.Timbrado);
    }

}
