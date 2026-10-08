using Millet.Facturacion.Application.Timbrado;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Application.Repp.Pendientes;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Repp;

public sealed class ReppPendienteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(Empresa));
    private static ReppPendiente Pendiente(IEnumerable<RelacionRepp>? relacion = null, string moneda = "MXN") =>
        new(Empresa, Cliente, Guid.NewGuid(), Guid.NewGuid(), null, 100m, moneda, new DateOnly(2026, 10, 28), "Prueba ficticia", relacion ?? []);
    private static FacturaVenta Factura(string metodo = "PPD", string rfc = "AAA010101AAA", string moneda = "MXN")
    {
        var factura = FacturaVenta.CrearBorrador(Empresa, "PRUEBA-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor(rfc, "Cliente ficticio", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio", "601", "97000"), metodo, "03", moneda,
            moneda == "MXN" ? null : 20m, 2026, 10, 7, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        factura.AgregarLinea(null, "01010101", "Servicio ficticio", "E48", 1, 100, 0, "01", null, null, null);
        factura.RecalcularTotales();
        factura.MarcarTimbradoEnProceso();
        factura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return factura;
    }
    private static ReppPendienteServicio Servicio(FacturacionDbContext db, decimal? tc = 20m) => new(db,
        new FakeClientesReadPort(new(Cliente, "AAA010101AAA", "Cliente ficticio", "601", "97000", "G03", "03", "PPD", "MXN", false)),
        new FakeCatalogosSatReadPort(), new Banco(tc));

    [Theory]
    [InlineData("2026-11-02T06:00:00Z", "Cerca del plazo")]
    [InlineData("2026-11-03T12:00:00Z", "Cerca del plazo")]
    [InlineData("2026-11-06T05:59:59Z", "Cerca del plazo")]
    [InlineData("2026-11-06T06:00:00Z", "Vencido")]
    [InlineData("2026-11-02T05:59:59Z", "En plazo")]
    public void Plazo_usa_dias_naturales_de_Mexico(string ahora, string alerta)
    {
        var limite = PlazoRepp.FechaLimite(new(2026, 10, 28));
        limite.Should().Be(new DateOnly(2026, 11, 5));
        PlazoRepp.Alerta(limite, DateTimeOffset.Parse(ahora)).Should().Be(alerta);
        PlazoRepp.FechaLimite(new(2026, 12, 31)).Should().Be(new DateOnly(2027, 1, 5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Evento_crea_un_pendiente_incluso_sin_facturas_y_no_timbra_tras_cinco_minutos(bool conFacturas)
    {
        using var db = Db();
        var payload = new PagoClienteConfirmadoPayload(Empresa, Ahora, null, Cliente, Guid.NewGuid(), Guid.NewGuid(),
            100m, "MXN", new(2026, 10, 28), "Prueba", conFacturas ? [new(Guid.NewGuid(), 100m)] : []);
        var comando = new EmitirReppDesdePagoConfirmadoCommand(Guid.NewGuid(), payload);
        await new EmitirReppDesdePagoConfirmadoHandler(db, new FakeClock(Ahora)).Handle(comando, default);
        var despues = new EmitirReppDesdePagoConfirmadoHandler(db, new FakeClock(Ahora.AddMinutes(5)));
        await despues.Handle(comando, default);
        await despues.Handle(comando with { EventoId = Guid.NewGuid() }, default);
        db.ChangeTracker.Clear();
        var p = await db.ReppPendientes.Include(p => p.Facturas).SingleAsync();
        p.Facturas.Count.Should().Be(conFacturas ? 1 : 0);
        p.Estado.Should().Be(EstadoReppPendiente.Pendiente);
        p.FormaPago.Should().Be("03");
        (await db.RecibosPago.CountAsync()).Should().Be(0);
        (await db.EventosProcesados.CountAsync()).Should().Be(2);
    }

    [Fact]
    public void Relacion_no_admite_suma_distinta_duplicados_ni_negativos()
    {
        var p = Pendiente();
        var id = Guid.NewGuid();
        foreach (var relacion in new RelacionRepp[][] { [], [new(id, 99)], [new(id, -1), new(Guid.NewGuid(), 101)], [new(id, 50), new(id, 50)] })
        {
            var actuar = () => p.Revisar("03", relacion);
            actuar.Should().Throw<BusinessRuleException>();
        }
        p.Revisar("01", [new(id, 100)]);
        p.Monto.Should().Be(100);
        p.Moneda.Should().Be("MXN");
        p.Revisado.Should().BeTrue();
    }

    [Theory]
    [InlineData("PUE", "AAA010101AAA", "MXN", 100)]
    [InlineData("PPD", "OTR010101AAA", "MXN", 100)]
    [InlineData("PPD", "AAA010101AAA", "USD", 100)]
    [InlineData("PPD", "AAA010101AAA", "MXN", 101)]
    public async Task Revision_rechaza_PUE_otro_cliente_moneda_o_sobrepago(string metodo, string rfc, string moneda, decimal importe)
    {
        using var db = Db();
        var f = Factura(metodo, rfc, moneda); db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var p = new ReppPendiente(Empresa, Cliente, Guid.NewGuid(), Guid.NewGuid(), null, importe, "MXN", new(2026, 10, 28), null, []);
        var accion = () => Servicio(db).ValidarAsync(p, "03", [new(f.Id, importe)], default);
        await accion.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task TC_historico_es_obligatorio_solo_para_moneda_extranjera()
    {
        using var db = Db();
        (await Servicio(db, null).TipoCambioAsync(Pendiente(), default)).Should().BeNull();
        var accion = () => Servicio(db, null).TipoCambioAsync(Pendiente(moneda: "USD"), default);
        (await accion.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("REPP_TC_FALTANTE");
        (await Servicio(db, 19.25m).TipoCambioAsync(Pendiente(moneda: "USD"), default)).Should().Be(19.25m);
    }

    [Theory]
    [InlineData(TimbradoEstado.Timbrado, true)]
    [InlineData(TimbradoEstado.Fallido, false)]
    [InlineData(TimbradoEstado.EnProceso, false)]
    public async Task Emision_reutiliza_handler_y_conserva_intento_sin_duplicar(TimbradoEstado estado, bool emitido)
    {
        using var db = Db();
        var f = Factura(); var p = Pendiente([new(f.Id, 100)]);
        db.FacturasVenta.Add(f); db.ReppPendientes.Add(p); await db.SaveChangesAsync();
        var fiscal = new Pac(estado);
        var sender = new EmisionSender(new EmitirReppHandler(db, new FakeSender(new ReservarFolioResponse("P-PRUEBA", 1, "")),
            new FakePeriodoContablePort(), fiscal, new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(Empresa), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora),
            new ValidadorReceptorFiscal(new FakeClientesReadPort(), new FakeCatalogosSatReadPort(), db)));
        var handler = new EmitirReppPendienteHandler(db, Servicio(db), new Banco(20m), sender);
        var resultado = await handler.Handle(new(p.Id), default);
        resultado.Emitido.Should().Be(emitido);
        db.ChangeTracker.Clear();
        var guardado = await db.ReppPendientes.SingleAsync();
        guardado.Estado.Should().Be(emitido ? EstadoReppPendiente.Emitido : EstadoReppPendiente.Pendiente);
        guardado.IntentoReciboPagoId.Should().NotBeNull();
        if (!emitido) guardado.UltimoErrorCodigo.Should().NotBeNull();
        await handler.Handle(new(p.Id), default);
        (await db.RecibosPago.CountAsync()).Should().Be(1);
        fiscal.Llamadas.Should().Be(1);
    }

    [Fact]
    public async Task Regla_fallida_guarda_error_y_no_crea_recibo()
    {
        using var db = Db(); var p = Pendiente(); db.ReppPendientes.Add(p); await db.SaveChangesAsync();
        var handler = new EmitirReppPendienteHandler(db, Servicio(db), new Banco(null), new FakeSender(new("P", 1, "")));
        var r = await handler.Handle(new(p.Id), default);
        r.Emitido.Should().BeFalse(); db.ChangeTracker.Clear();
        (await db.ReppPendientes.SingleAsync()).UltimoErrorCodigo.Should().Be("REPP_RELACION_INVALIDA");
        (await db.RecibosPago.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void Descartar_exige_motivo_y_cierra_el_pendiente()
    {
        var p = Pendiente();
        var sinMotivo = () => p.Descartar(" "); sinMotivo.Should().Throw<BusinessRuleException>();
        p.Descartar("Factura PUE a crédito"); p.Estado.Should().Be(EstadoReppPendiente.Descartado);
        var editar = () => p.Revisar("03", [new(Guid.NewGuid(), 100)]); editar.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public async Task Bandeja_respeta_empresa_y_cuenta_plazos_solo_de_pendientes()
    {
        using var db = Db();
        var propio = Pendiente();
        propio.RegistrarError("PRUEBA", "Error ficticio");
        db.ReppPendientes.AddRange(propio, new ReppPendiente(Guid.NewGuid(), Cliente, Guid.NewGuid(), Guid.NewGuid(), null,
            100, "MXN", new(2026, 10, 28), null, []));
        await db.SaveChangesAsync();
        var handler = new ReppPendienteQueryHandler(db, Servicio(db), new FakeClientesReadPort(), new Banco(null), new FakeClock(Ahora));
        var resultado = await handler.Handle(new ReppPendientesQuery(), default);
        resultado.Items.Should().ContainSingle().Which.Id.Should().Be(propio.Id);
        resultado.Kpis.Should().Be(new ReppPendientesKpis(1, 1, 0, 1));
    }

    [Fact]
    public void Resultado_incierto_no_admite_editar_ni_generar_un_nuevo_intento()
    {
        var p = Pendiente();
        p.RegistrarError("REPP_RESULTADO_INCIERTO", "Consultar PAC");
        var revisar = () => p.Revisar("03", [new(Guid.NewGuid(), 100m)]);
        revisar.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("REPP_RESULTADO_INCIERTO");
    }

    private sealed class Banco(decimal? tc) : IReppBancarioReadPort
    {
        public Task<decimal?> TipoCambioAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken) => Task.FromResult(tc);
        public Task<Guid> SucursalEmisoraAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
    }
    private sealed class Pac(TimbradoEstado estado) : ICfdiTimbradoPort
    {
        public int Llamadas { get; private set; }
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken)
        {
            Llamadas++;
            return Task.FromResult(new TimbradoResultado(estado, estado == TimbradoEstado.Timbrado ? Guid.NewGuid().ToString() : null,
                null, null, null, Ahora, null, "<prueba/>", estado == TimbradoEstado.Fallido ? "PAC_PRUEBA" : null, "Resultado simulado"));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class EmisionSender(EmitirReppHandler handler) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            (TResponse)(object)await handler.Handle((EmitirReppCommand)(object)request, cancellationToken);
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
