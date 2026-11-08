using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Facturas;

/// <summary>
/// F4-PR2 — emisión de factura final con amortización de anticipos (M2+M3):
/// la NC de amortización se autogenera, se timbra y reduce el saldo. U-1: si la
/// NC no timbra, la factura timbrada se guarda y la NC queda pendiente.
/// </summary>
public sealed class AmortizacionAnticipoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
    private const string RfcCliente = "AAA010101AAA";

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    /// <summary>ISender que reserva folios únicos por llamada (evita choque del índice (sucursal, folio)).</summary>
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

    /// <summary>Timbra con un UUID distinto por llamada (factura ≠ anticipo ≠ NC), como el PAC real.</summary>
    private sealed class FakeFiscalUuidsUnicos : ICfdiTimbradoPort
    {
        private int _n;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _n++;
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Timbrado, $"00000000-0000-0000-0000-{_n:D12}", "S", "S", "0",
                Ahora, "SAT970701NN3", $"<cfdi serie=\"{emision.Serie}\" folio=\"{emision.Folio}\" fake=\"true\" />", null, null));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    /// <summary>Timbra la factura (1ª llamada) pero falla la NC de amortización (2ª en adelante).</summary>
    private sealed class FakeFiscalFallaEnLaNc : ICfdiTimbradoPort
    {
        private int _llamadas;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _llamadas++;
            if (_llamadas == 1)
                return Task.FromResult(new TimbradoResultado(
                    TimbradoEstado.Timbrado, "11111111-1111-1111-1111-111111111111", "S", "S", "0",
                    Ahora, "SAT970701NN3", $"<cfdi serie=\"{emision.Serie}\" folio=\"{emision.Folio}\" fake=\"true\" />", null, null));
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Fallido, null, null, null, null, null, null, null, "PAC-99", "PAC caído"));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    /// <summary>B2/B5: timbra el anticipo (1ª llamada) y el PAC rechaza la factura final con su motivo.</summary>
    private sealed class FakeFiscalRechazaLaFactura : ICfdiTimbradoPort
    {
        private int _llamadas;
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            _llamadas++;
            if (_llamadas == 1)
                return Task.FromResult(new TimbradoResultado(
                    TimbradoEstado.Timbrado, "33333333-3333-3333-3333-333333333333", "S", "S", "0",
                    Ahora, "SAT970701NN3", $"<cfdi serie=\"{emision.Serie}\" folio=\"{emision.Folio}\" fake=\"true\" />", null, null));
            return Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Fallido, null, null, null, null, null, null, null, "400",
                "Complement.ComercioExterior.Receptor.Domicilio.Calle: 'calle' no debe estar vacío."));
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            Task.FromResult(new CancelacionResultado(CancelacionEstado.Aceptada, "ok", null));
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(true, "Vigente", true, null));
    }

    private static FakeEmpresaFiscalReadPort EmpresasFiscal(Guid empresaId) =>
        new(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));

    private static EmitirFacturaAnticipoHandler EmitirAnticipoHandler(
        FacturacionDbContext db, Guid empresaId, ISender sender, ICfdiTimbradoPort? fiscal = null) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), fiscal ?? new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), EmpresasFiscal(empresaId), new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    private static EmitirFacturaVentaHandler FacturaHandler(FacturacionDbContext db, Guid empresaId, ISender sender, ICfdiTimbradoPort fiscal,
        FakeIntegrationEventPublisher? eventos = null) =>
        new(db, sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(empresaId), eventos ?? new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    private static EmitirFacturaAnticipoCommand AnticipoCommand(decimal montoBase = 1000m) => new(
        Guid.NewGuid(), Guid.NewGuid(), RfcCliente, "Cliente Maquila", "601", "97000", "G03", "MEX",
        "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, montoBase, 0.16m, null, null, "AW-7", null, null);

    private static EmitirFacturaVentaCommand FacturaCommand(IReadOnlyList<AnticipoAAmortizar> anticipos, string rfc = RfcCliente) => new(
        SucursalId: Guid.NewGuid(),
        ReceptorRfc: rfc, ReceptorNombre: "Cliente Maquila", ReceptorRegimenFiscal: "601",
        ReceptorCodigoPostal: "97000", ReceptorUsoCfdi: "G03", ReceptorPais: "MEX",
        RfcEmisor: "BBB010101BBB", RegimenFiscalEmisor: "601",
        MetodoPago: "PUE", FormaPago: "03", Moneda: "MXN", TipoCambio: null,
        CanalVenta: (short)7, ComportamientoFiscal: ComportamientoFiscal.ConAnticipo,
        ObraId: null, ObraNombre: null, FacturaAgrupada: false,
        Lineas: [new EmitirFacturaVentaLinea(null, "01010101", "Servicio maquila", "E48", 1m, 10000m, 0m, "02", 0.16m, null, null)],
        Anticipos: anticipos);

    [Fact]
    public async Task Emitir_con_anticipo_genera_NC_timbrada_y_reduce_saldo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender, fiscal).Handle(AnticipoCommand(), CancellationToken.None);

        var resp = await FacturaHandler(db, empresaId, sender, fiscal)
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m, "  Aplicación del anticipo de obra  ")]),
                CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.NotasCreditoAmortizacion.Should().HaveCount(1);
        resp.NotasCreditoAmortizacion![0].Total.Should().Be(500m);
        resp.NotasCreditoAmortizacion![0].SaldoAnticipoRestante.Should().Be(660m); // 1160 - 500

        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        nc.Estado.Should().Be(EstadoTimbrado.Timbrado);
        nc.Tipo.Should().Be(TipoComprobante.Egreso);
        nc.AnticipoOrigenId.Should().Be(anticipo.AnticipoId);
        nc.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "07" && r.UuidRelacionado == resp.Uuid); // solo la factura final
        nc.Comentarios.Should().Be("Aplicación del anticipo de obra"); // FMT-6: comentario libre capturado al emitir

        var factura = await db.FacturasVenta.Include(f => f.Relaciones).SingleAsync();
        factura.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "07");

        var anticipoBd = await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync();
        anticipoBd.MontoAmortizado.Should().Be(500m);
        anticipoBd.Saldo.Should().Be(660m);
        anticipoBd.Estado.Should().Be(EstadoAnticipo.Abierto);
        anticipoBd.Vinculaciones.Single().NcAmortizacionId.Should().Be(nc.Id);
    }

    [Fact]
    public async Task Emitir_amortizando_el_total_deja_el_anticipo_Amortizado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalUuidsUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender, fiscal).Handle(AnticipoCommand(), CancellationToken.None);

        await FacturaHandler(db, empresaId, sender, fiscal)
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 1160m)]), CancellationToken.None);

        var anticipoBd = await db.Anticipos.SingleAsync();
        anticipoBd.Saldo.Should().Be(0m);
        anticipoBd.Estado.Should().Be(EstadoAnticipo.Amortizado);
    }

    [Fact]
    public async Task Emitir_con_anticipo_de_otro_cliente_es_rechazado_sin_timbrar()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender).Handle(AnticipoCommand(), CancellationToken.None);

        var act = () => FacturaHandler(db, empresaId, sender, new FakeFiscalApiClient())
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m)], rfc: "CCC010101CCC"), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_CLIENTE_DISTINTO");
        (await db.FacturasVenta.CountAsync()).Should().Be(0); // no se timbró nada
    }

    [Fact]
    public async Task Emitir_con_importe_mayor_al_saldo_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender).Handle(AnticipoCommand(), CancellationToken.None);

        var act = () => FacturaHandler(db, empresaId, sender, new FakeFiscalApiClient())
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 5000m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
    }

    [Fact]
    public async Task Si_la_NC_no_timbra_la_factura_se_guarda_y_la_NC_queda_pendiente_U1()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender).Handle(AnticipoCommand(), CancellationToken.None);
        var eventos = new FakeIntegrationEventPublisher();

        var resp = await FacturaHandler(db, empresaId, sender, new FakeFiscalFallaEnLaNc(), eventos)
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);

        // U-1 (86e3yxn2p): la factura timbrada no se pierde; la NC queda pendiente con el motivo.
        resp.Estado.Should().Be("Timbrado");
        resp.NotasCreditoAmortizacion!.Single().Estado.Should().Be("TimbradoFallido");
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.Timbrado);
        var nc = await db.NotasCredito.SingleAsync();
        (nc.Estado, nc.TimbradoErrorCodigo).Should().Be((EstadoTimbrado.TimbradoFallido, "PAC-99"));
        eventos.Publicados.Should().NotContain(e => e is Millet.Facturacion.Application.Integration.NotaCreditoTimbradaIntegrationEvent);

        // El importe queda comprometido con la factura sin reducir el saldo.
        var anticipoBd = await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync();
        anticipoBd.Saldo.Should().Be(1160m);
        anticipoBd.MontoAmortizado.Should().Be(0m);
        anticipoBd.SaldoDisponible.Should().Be(660m);
        anticipoBd.Vinculaciones.Single().NcAmortizacionId.Should().BeNull();
    }

    [Fact]
    public async Task Criterio3_con_dos_anticipos_si_falla_la_segunda_NC_quedan_la_factura_y_la_primera_U1()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var uuids = new FakeFiscalUuidsUnicos();
        var a1 = await EmitirAnticipoHandler(db, empresaId, sender, uuids).Handle(AnticipoCommand(), CancellationToken.None);
        var a2 = await EmitirAnticipoHandler(db, empresaId, sender, uuids).Handle(AnticipoCommand(), CancellationToken.None);

        // 1 = factura, 2 = NC del primer anticipo, 3 = NC del segundo (falla).
        var fiscal = new NotasCredito.NcPendienteTimbrarTests.FakeFiscalGuion(3);
        var resp = await FacturaHandler(db, empresaId, sender, fiscal)
            .Handle(FacturaCommand([new AnticipoAAmortizar(a1.AnticipoId, 500m), new AnticipoAAmortizar(a2.AnticipoId, 300m)]),
                CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.NotasCreditoAmortizacion!.Select(n => n.Estado).Should().Equal("Timbrado", "TimbradoFallido");
        var ncs = await db.NotasCredito.AsNoTracking().ToListAsync();
        ncs.Single(n => n.AnticipoOrigenId == a1.AnticipoId).Estado.Should().Be(EstadoTimbrado.Timbrado);
        var pendiente = ncs.Single(n => n.AnticipoOrigenId == a2.AnticipoId);
        pendiente.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
        (await db.Anticipos.SingleAsync(a => a.Id == a1.AnticipoId)).MontoAmortizado.Should().Be(500m);
        (await db.Anticipos.SingleAsync(a => a.Id == a2.AnticipoId)).MontoAmortizado.Should().Be(0m);

        // Reintento desde la bandeja: la NC timbra y entonces sí amortiza el anticipo.
        var reintento = new Millet.Facturacion.Application.Timbrado.ReintentarTimbrado.ReintentarTimbradoHandler(
            db, sender, new FakePeriodoContablePort(true), fiscal, new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(),
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));
        (await reintento.Handle(new(pendiente.Id), CancellationToken.None)).Estado.Should().Be("Timbrado");
        var a2Bd = await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync(a => a.Id == a2.AnticipoId);
        a2Bd.MontoAmortizado.Should().Be(300m);
        a2Bd.Vinculaciones.Single().NcAmortizacionId.Should().Be(pendiente.Id);
    }

    [Fact]
    public async Task Descartar_la_NC_de_amortizacion_pendiente_libera_el_importe_comprometido_U1()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender).Handle(AnticipoCommand(), CancellationToken.None);
        var resp = await FacturaHandler(db, empresaId, sender, new FakeFiscalFallaEnLaNc())
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);

        await new Millet.Facturacion.Application.Timbrado.DescartarComprobante.DescartarComprobanteHandler(db, new FakeClock(Ahora))
            .Handle(new(resp.NotasCreditoAmortizacion!.Single().Id), CancellationToken.None);

        var anticipoBd = await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync();
        anticipoBd.SaldoDisponible.Should().Be(1160m);
        anticipoBd.Vinculaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Emitir_sin_anticipos_no_genera_NC()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();

        var resp = await FacturaHandler(db, empresaId, sender, new FakeFiscalApiClient())
            .Handle(FacturaCommand([]), CancellationToken.None);

        resp.NotasCreditoAmortizacion.Should().BeNull();
        (await db.NotasCredito.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task B2_B5_factura_con_anticipo_rechazada_por_el_PAC_queda_Descartada_con_el_motivo_sin_tocar_el_anticipo()
    {
        // Reproduce FCNK-000001 de la prueba S3-4: el PAC rechaza la factura que amortiza el anticipo.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var sender = new FakeSenderFoliosUnicos();
        var fiscal = new FakeFiscalRechazaLaFactura();
        var anticipo = await EmitirAnticipoHandler(db, empresaId, sender, fiscal).Handle(AnticipoCommand(), CancellationToken.None);
        var saldoAntes = (await db.Anticipos.SingleAsync()).Saldo;

        var act = () => FacturaHandler(db, empresaId, sender, fiscal)
            .Handle(FacturaCommand([new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]), CancellationToken.None);

        var error = (await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "FACTURA_NO_TIMBRADA_PARA_AMORTIZAR")).Which;
        error.Message.Should().Contain("No se pueden amortizar anticipos")
            .And.Contain("Motivo del PAC (400)")
            .And.Contain("'calle' no debe estar vacío");
        db.ChangeTracker.Clear();
        var factura = await db.FacturasVenta.SingleAsync();
        factura.Estado.Should().Be(EstadoTimbrado.Descartada);
        factura.Folio.Should().Be("DOC-000002");
        factura.TimbradoErrorCodigo.Should().Be("400");
        factura.TimbradoErrorMensaje.Should().Contain("'calle' no debe estar vacío");
        (await db.ContingenciasTimbrado.SingleAsync()).ComprobanteFolio.Should().Be("DOC-000002");
        var anticipoDespues = await db.Anticipos.Include(a => a.Vinculaciones).SingleAsync();
        anticipoDespues.Saldo.Should().Be(saldoAntes);
        anticipoDespues.Vinculaciones.Should().BeEmpty();
        (await db.NotasCredito.AnyAsync()).Should().BeFalse();
    }
}
