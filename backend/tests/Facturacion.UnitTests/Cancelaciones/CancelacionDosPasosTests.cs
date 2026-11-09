using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Cancelaciones.AutorizarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.Queries;
using Millet.Facturacion.Application.Cancelaciones.RechazarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.RetirarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

/// <summary>F6.5: Ingresos solicita (sin PAC), el Contador General autoriza (PAC) o rechaza con motivo; Ingresos puede retirar.</summary>
public sealed class CancelacionDosPasosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Rfc = "AAA010101AAA";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class FakeFiscalRechaza : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Rechazada, "Rechazada", "El receptor rechazó."));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private sealed class FakeSenderFoliosUnicos : ISender
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

    private sealed class FakeFiscalUuidsUnicos : ICfdiTimbradoPort
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

    private static readonly Guid Ingresos = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();

    /// <summary>PAC que cuenta las llamadas: solicitar no debe llamarlo.</summary>
    private sealed class FakeFiscalContador(ICfdiTimbradoPort interno) : ICfdiTimbradoPort
    {
        public int Cancelaciones { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => interno.TimbrarAsync(emision, ct);
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct)
        {
            Cancelaciones++;
            return interno.CancelarAsync(s, ct);
        }
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) => interno.ConsultarEstatusAsync(s, ct);
    }

    private static SolicitarCancelacionHandler Handler(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeEmpresaContext(empresaId), new FakeUserContext(Ingresos, "Ingresos"), new FakeClock(Ahora));

    private static AutorizarCancelacionHandler Autorizador(FacturacionDbContext db, ICfdiTimbradoPort? fiscal = null, Guid? usuario = null) =>
        new(db, fiscal ?? new FakeFiscalApiClient(), new FakeIntegrationEventPublisher(),
            new FakeUserContext(usuario ?? Contador, "Contador"), new FakeClock(Ahora));

    private static Task<SolicitarCancelacionResponse> SolicitarAsync(FacturacionDbContext db, Guid empresaId, Guid comprobanteId, string motivo = "02", string? sustituto = null) =>
        Handler(db, empresaId).Handle(new SolicitarCancelacionCommand(comprobanteId, motivo, sustituto), CancellationToken.None);

    private static async Task<(FacturaVenta, PedidoFacturable)> SembrarFacturaConPedidoAsync(FacturacionDbContext db, Guid empresaId)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresaId, "AW-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente", (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        var receptor = new DatosFiscalesReceptor(Rfc, "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        pedido.MarcarFacturado(fv.Id);
        db.PedidosFacturables.Add(pedido);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return (fv, pedido);
    }

    [Fact]
    public async Task Solicitar_queda_pendiente_de_autorizacion_sin_llamar_al_PAC()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, pedido) = await SembrarFacturaConPedidoAsync(db, empresaId);

        var resp = await SolicitarAsync(db, empresaId, fv.Id);

        resp.EstadoComprobante.Should().Be("Timbrado");
        resp.EstadoSolicitud.Should().Be("PendienteAutorizacion");
        var solicitud = await db.SolicitudesCancelacion.SingleAsync();
        (solicitud.SolicitadaPorId, solicitud.SolicitadaPorNombre).Should().Be((Ingresos, "Ingresos"));
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Facturado);
    }

    [Fact]
    public async Task No_se_aceptan_dos_solicitudes_pendientes_del_mismo_comprobante()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, fv.Id);

        var act = () => SolicitarAsync(db, empresaId, fv.Id, "03");
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_YA_SOLICITADA");
    }

    [Fact]
    public async Task Autorizar_envia_al_PAC_cancela_y_libera_el_pedido()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, fv.Id);
        var fiscal = new FakeFiscalContador(new FakeFiscalApiClient());

        var resp = await Autorizador(db, fiscal).Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);

        fiscal.Cancelaciones.Should().Be(1);
        resp.EstadoComprobante.Should().Be("Cancelado");
        resp.EstadoSolicitud.Should().Be("Aceptada");
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.Cancelado);
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Importado);
        (await db.SolicitudesCancelacion.SingleAsync()).DecididaPorId.Should().Be(Contador);
    }

    [Fact]
    public async Task Quien_solicito_no_puede_autorizar_aunque_tenga_el_permiso()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, fv.Id);
        var fiscal = new FakeFiscalContador(new FakeFiscalApiClient());

        var act = () => Autorizador(db, fiscal, Ingresos).Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_AUTOAUTORIZACION");
        fiscal.Cancelaciones.Should().Be(0);
    }

    [Fact]
    public async Task Rechazar_con_motivo_deja_la_factura_vigente_y_el_motivo_visible()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, fv.Id);

        var resp = await new RechazarCancelacionHandler(db, new FakeUserContext(Contador, "Contador"), new FakeClock(Ahora))
            .Handle(new RechazarCancelacionCommand(fv.Id, "Se corrige con nota de crédito"), CancellationToken.None);

        resp.EstadoComprobante.Should().Be("Timbrado");
        resp.EstadoSolicitud.Should().Be("RechazadaPorAutorizador");
        var consulta = await new ConsultarCancelacionHandler(db).Handle(new ConsultarCancelacionQuery(fv.Id), CancellationToken.None);
        (consulta.EstadoComprobante, consulta.MotivoRechazo, consulta.DecididaPor, consulta.SolicitadaPor)
            .Should().Be(("Timbrado", "Se corrige con nota de crédito", "Contador", "Ingresos"));
    }

    [Fact]
    public void Rechazar_sin_motivo_no_pasa_la_validacion()
    {
        var resultado = new RechazarCancelacionValidator().Validate(new RechazarCancelacionCommand(Guid.NewGuid(), ""));
        resultado.IsValid.Should().BeFalse();
        // O3: el mensaje del 400 sale en español.
        resultado.Errors.Should().ContainSingle().Which.ErrorMessage
            .Should().Be("Indica el motivo por el que no se autoriza la cancelación.");
    }

    [Fact]
    public async Task Retirar_deja_la_factura_vigente_y_permite_solicitar_de_nuevo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, fv.Id);

        var resp = await new RetirarCancelacionHandler(db, new FakeClock(Ahora))
            .Handle(new RetirarCancelacionCommand(fv.Id), CancellationToken.None);

        resp.EstadoSolicitud.Should().Be("Retirada");
        (await SolicitarAsync(db, empresaId, fv.Id, "03")).EstadoSolicitud.Should().Be("PendienteAutorizacion");
    }

    [Fact]
    public async Task Autorizar_sin_solicitud_pendiente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);

        var act = () => Autorizador(db).Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);
        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "CANCELACION_PENDIENTE_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Sustitucion_motivo_01_autorizada_envia_el_UUID_sustituto_al_PAC()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        const string sustituto = "22222222-2222-2222-2222-222222222222";
        await SolicitarAsync(db, empresaId, fv.Id, "01", sustituto);
        var fiscal = new FakeFiscalCapturaCancelacion();

        await Autorizador(db, fiscal).Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);

        (fiscal.Ultima?.MotivoSat, fiscal.Ultima?.UuidSustituto).Should().Be(("01", sustituto));
    }

    [Fact]
    public async Task Bandeja_muestra_las_pendientes_con_factura_motivo_y_solicitante()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, fv.Id);

        var bandeja = await new BandejaCancelacionesHandler(db).Handle(new BandejaCancelacionesQuery(null), CancellationToken.None);

        var fila = bandeja.Should().ContainSingle().Subject;
        (fila.ComprobanteId, fila.TipoComprobante, fila.Folio, fila.MotivoSat, fila.SolicitadaPor, fila.EstadoSolicitud)
            .Should().Be((fv.Id, "FacturaVenta", "F-1", "02", "Ingresos", "PendienteAutorizacion"));
        fila.SolicitadaPorId.Should().Be(Ingresos); // O2: la bandeja oculta la decisión a quien solicitó.
        (await new BandejaCancelacionesHandler(db).Handle(
            new BandejaCancelacionesQuery(EstadoSolicitudCancelacion.Aceptada), CancellationToken.None)).Should().BeEmpty();
    }

    private sealed class FakeFiscalCapturaCancelacion : ICfdiTimbradoPort
    {
        public CancelacionSolicitud? Ultima { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => throw new NotImplementedException();
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct)
        {
            Ultima = s;
            return Task.FromResult(new CancelacionResultado(CancelacionEstado.EnProceso, "EnProceso", null));
        }
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    [Fact]
    public async Task Cancelar_no_timbrado_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var receptor = new DatosFiscalesReceptor(Rfc, "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();

        var act = () => SolicitarAsync(db, empresaId, fv.Id);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COMPROBANTE_NO_CANCELABLE");
    }

    [Fact]
    public async Task Cancelar_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => SolicitarAsync(db, empresaId, Guid.NewGuid());
        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "COMPROBANTE_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Si_el_PAC_rechaza_el_comprobante_vuelve_a_Timbrado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);

        await SolicitarAsync(db, empresaId, fv.Id);

        var resp = await Autorizador(db, new FakeFiscalRechaza()).Handle(new AutorizarCancelacionCommand(fv.Id), CancellationToken.None);

        resp.EstadoComprobante.Should().Be("Timbrado");
        resp.EstadoSolicitud.Should().Be("Rechazada");
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.Timbrado);
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Facturado);
    }

    [Fact]
    public async Task Cancelar_motivo_01_sin_sustituto_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (fv, _) = await SembrarFacturaConPedidoAsync(db, empresaId);

        var act = () => SolicitarAsync(db, empresaId, fv.Id, "01");
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CANCELACION_SUSTITUTO_REQUERIDO");
    }

    [Fact]
    public async Task Cancelar_anticipo_con_NC_de_amortizacion_vigente_es_bloqueado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();

        // Anticipo emitido y luego amortizado contra una factura final (deja NC vigente).
        var empresasFiscal = new FakeEmpresaFiscalReadPort(
            new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));
        var emitAnticipoHandler = new EmitirFacturaAnticipoHandler(db, sender, new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), fiscal, new FakeCfdiRepositorioPort(), empresasFiscal,
            new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));
        var anticipo = await emitAnticipoHandler.Handle(new EmitirFacturaAnticipoCommand(
            Guid.NewGuid(), Guid.NewGuid(), Rfc, "Cliente", "601", "97000", "G03", "MEX",
            "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, 1000m, 0.16m, null, null, "AW-1", null, null),
            CancellationToken.None);

        var emitFacturaHandler = new EmitirFacturaVentaHandler(db, sender, new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), fiscal, new FakeCfdiRepositorioPort(), empresasFiscal,
            new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));
        await emitFacturaHandler.Handle(new EmitirFacturaVentaCommand(
            Guid.NewGuid(), Rfc, "Cliente", "601", "97000", "G03", "MEX", "BBB010101BBB", "601",
            "PUE", "03", "MXN", null, (short)7, ComportamientoFiscal.ConAnticipo, null, null, false,
            [new EmitirFacturaVentaLinea(null, "01010101", "Maquila", "E48", 1m, 5000m, 0m, "02", 0.16m, null, null)],
            [new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);

        // Cancelar el CFDI del anticipo debe bloquearse: hay NC de amortización vigente.
        var act = () => SolicitarAsync(db, empresaId, anticipo.FacturaAnticipoId);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_AMORTIZADO_NCS_VIGENTES");
    }

    [Fact]
    public async Task Cancelar_anticipo_con_NC_de_devolucion_vigente_es_bloqueado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var empresasFiscal = new FakeEmpresaFiscalReadPort(
            new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));
        var anticipo = await new EmitirFacturaAnticipoHandler(db, new FakeSenderFoliosUnicos(), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), new FakeFiscalUuidsUnicos(), new FakeCfdiRepositorioPort(), empresasFiscal,
            new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db))
            .Handle(new EmitirFacturaAnticipoCommand(
                Guid.NewGuid(), Guid.NewGuid(), Rfc, "Cliente", "601", "97000", "G03", "MEX",
                "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, 1000m, 0.16m, null, null, "AW-1", null, null),
                CancellationToken.None);

        // F6.3: NC de devolución en dinero del anticipo (relación 01 a su CFDI), aún vigente.
        db.NotasCredito.Add(NotaCredito.CrearDevolucionAnticipo(empresaId, "NC-1", 1, Guid.NewGuid(), null,
            new DatosFiscalesReceptor(Rfc, "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"), "03", "MXN", null, 2026, 10, 1,
            anticipo.AnticipoId, 116m, 0.16m, "BBVA", "032180000118359719", "Cliente SA"));
        await db.SaveChangesAsync();

        var act = () => SolicitarAsync(db, empresaId, anticipo.FacturaAnticipoId);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_DEVUELTO_NCS_VIGENTES");
    }

    [Fact]
    public async Task Bandeja_todos_trae_las_solicitudes_en_cualquier_estado_B3()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var (pendiente, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, pendiente.Id);
        var (decidida, _) = await SembrarFacturaConPedidoAsync(db, empresaId);
        await SolicitarAsync(db, empresaId, decidida.Id);
        await Autorizador(db).Handle(new AutorizarCancelacionCommand(decidida.Id), CancellationToken.None);

        var handler = new BandejaCancelacionesHandler(db);
        var soloPendientes = await handler.Handle(new BandejaCancelacionesQuery(null), CancellationToken.None);
        var todas = await handler.Handle(new BandejaCancelacionesQuery(null, Todos: true), CancellationToken.None);

        soloPendientes.Select(f => f.ComprobanteId).Should().Equal(pendiente.Id);
        todas.Select(f => f.ComprobanteId).Should().BeEquivalentTo([pendiente.Id, decidida.Id]);
        todas.Single(f => f.ComprobanteId == decidida.Id).EstadoSolicitud.Should().NotBe("PendienteAutorizacion");
    }
}
