using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>
/// Tests del reintento de timbrado (PR-B): FSM TimbradoFallido → Borrador →
/// re-timbre del MISMO comprobante, guard de códigos ambiguos y réplica de
/// los efectos post-éxito (evento + asiento) que el intento fallido omitió.
/// </summary>
public sealed class ReintentarTimbradoHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId, string? nombreBase = null) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(nombreBase ?? Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    /// <summary>PAC que rechaza siempre con un código dado (rechazo limpio).</summary>
    private sealed class FakeFiscalRechaza(string codigo) : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) =>
            Task.FromResult(new TimbradoResultado(
                TimbradoEstado.Fallido, null, null, null, null, null, null, null,
                codigo, $"rechazo {codigo}"));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private static Millet.Facturacion.Domain.Repp.ReciboPago CrearReppFallido(
        FacturacionDbContext db, Guid empresaId)
    {
        var repp = Millet.Facturacion.Domain.Repp.ReciboPago.CrearBorrador(
            empresaId, "P-000001", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), 2026, 7, Ahora, "MXN");
        // Con ImpuestosDR (P9-H8): fuerza el ThenInclude(f => f.Impuestos) del
        // reintento y el prorrateo del complemento de pago.
        repp.AgregarFacturaPagada(
            Guid.NewGuid(), "UUID-1", 1, "MXN", 580m, 1160m, "03", null, null, null, null, null,
            "02", 1160m, [new Millet.Facturacion.Domain.Repp.ImpuestoFacturaPagada(
                "002", "Tasa", 0.16m, EsRetencion: false, BaseGravable: 1000m)]);
        repp.MarcarTimbradoEnProceso();
        repp.MarcarTimbradoFallido("CFDI40149", "rechazo CFDI40149");
        db.RecibosPago.Add(repp);
        db.SaveChanges();
        return repp;
    }

    /// <summary>
    /// P9-H3: el reintento de un ReciboPago (REPP tipo P) fallido debe timbrar
    /// end-to-end — carga polimórfica + Include de FacturasPagadas.Impuestos
    /// (que #664 agregó) + re-timbre + evento. Este camino no tenía cobertura
    /// ("nunca ejercitado"), que era la razón por la que el fallo pasó
    /// desapercibido.
    /// </summary>
    [Fact]
    public async Task Reintento_ReciboPago_fallido_timbra_con_impuestos_y_publica_evento()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var repp = CrearReppFallido(db, empresaId);
        var eventos = new FakeIntegrationEventPublisher();

        var r = await Handler(db, eventos: eventos)
            .Handle(new ReintentarTimbradoCommand(repp.Id), CancellationToken.None);

        r.Tipo.Should().Be("ReciboPago");
        r.Estado.Should().Be("Timbrado");
        r.Folio.Should().Be("P-000001");
        r.Uuid.Should().NotBeNullOrWhiteSpace();
        r.TimbradoErrorCodigo.Should().BeNull();

        repp.Estado.Should().Be(EstadoTimbrado.Timbrado);
        repp.FacturasPagadas.Single().Impuestos.Should().ContainSingle();
        eventos.Publicados.OfType<Millet.Facturacion.Application.Integration.ReciboPagoTimbradoIntegrationEvent>()
            .Should().ContainSingle().Which.ReciboPagoId.Should().Be(repp.Id);
    }

    private static FacturaVenta CrearFacturaFallida(
        FacturacionDbContext db, Guid empresaId, string errorCodigo = "CFDI40139")
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "COTT-2026-000001", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 7,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Vidrio", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbradoFallido(errorCodigo, $"rechazo {errorCodigo}");
        db.FacturasVenta.Add(fv);
        db.SaveChanges();
        return fv;
    }

    private static ReintentarTimbradoHandler Handler(
        FacturacionDbContext db,
        ICfdiTimbradoPort? fiscal = null,
        FakeIntegrationEventPublisher? eventos = null,
        FakeContabilidadAsientoPort? contabilidad = null,
        bool periodoAbierto = true) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-000001", 1, "")),
            new FakePeriodoContablePort(periodoAbierto), fiscal ?? new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), eventos ?? new FakeIntegrationEventPublisher(),
            contabilidad ?? new FakeContabilidadAsientoPort(), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    private sealed class EmisorCorregido : Millet.Facturacion.Domain.Ports.IEmpresaFiscalReadPort
    {
        public Task<Millet.Facturacion.Domain.Ports.EmpresaFiscalLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Millet.Facturacion.Domain.Ports.EmpresaFiscalLectura?>(new(id, "EKU9003173C9", "ESCUELA KEMPER URGATE", "601", .16m, "42501"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
    }
    [Fact]
    public async Task PRUA_reintento_reconstruye_emisor_con_datos_corregidos_y_conserva_folio()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var factura = CrearFacturaFallida(db, empresaId);
        var handler = new ReintentarTimbradoHandler(db, new FakeSender(new ReservarFolioResponse("SIN-NUEVO-FOLIO", 2, "")),
            new FakePeriodoContablePort(true), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db), empresaFiscal: new EmisorCorregido());
        var respuesta = await handler.Handle(new(factura.Id), default);
        respuesta.Folio.Should().Be("COTT-2026-000001");
        factura.NombreEmisor.Should().Be("ESCUELA KEMPER URGATE");
        factura.Estado.Should().Be(EstadoTimbrado.Timbrado);
        Action cambiar = () => factura.ActualizarEmisorParaReintento(new("AAA010101AAA", "Otro", "601", "97000"));
        cambiar.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public async Task Reintento_exitoso_timbra_mismo_folio_limpia_error_y_publica_efectos()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId);
        var eventos = new FakeIntegrationEventPublisher();
        var contabilidad = new FakeContabilidadAsientoPort();

        var r = await Handler(db, eventos: eventos, contabilidad: contabilidad)
            .Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        r.Estado.Should().Be("Timbrado");
        r.Folio.Should().Be("COTT-2026-000001"); // mismo folio interno — no se re-emite
        r.Uuid.Should().NotBeNullOrWhiteSpace();
        r.TimbradoErrorCodigo.Should().BeNull();
        r.Tipo.Should().Be("FacturaVenta");

        fv.Estado.Should().Be(EstadoTimbrado.Timbrado);
        eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Should().ContainSingle()
            .Which.FacturaVentaId.Should().Be(fv.Id);
        contabilidad.Veces.Should().Be(1);
    }

    [Fact]
    public async Task Reintento_que_vuelve_a_fallar_reemplaza_el_error_sin_efectos()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId, "CFDI40139");
        var eventos = new FakeIntegrationEventPublisher();

        var r = await Handler(db, fiscal: new FakeFiscalRechaza("CFDI40148"), eventos: eventos)
            .Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        r.Estado.Should().Be("TimbradoFallido");
        r.TimbradoErrorCodigo.Should().Be("CFDI40148"); // el error nuevo reemplaza al viejo
        eventos.Publicados.Should().BeEmpty();
        // Sigue reintentable: la FSM regresó a TimbradoFallido.
        fv.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
    }

    [Fact]
    public async Task Reintento_de_comprobante_no_fallido_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId);
        await Handler(db).Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None); // queda Timbrado

        var act = () => Handler(db).Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("COMPROBANTE_NO_REINTENTABLE");
    }

    [Theory]
    [InlineData("PAC_TIMEOUT")]
    [InlineData("PAC_SIN_RESPUESTA")]
    [InlineData("PAC_RESPUESTA_INCOMPLETA")]
    public async Task Reintento_de_fallo_ambiguo_exige_confirmacion(string codigo)
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId, codigo);

        // Sin confirmación: rechazado (riesgo de timbre duplicado).
        var act = () => Handler(db).Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("REINTENTO_REQUIERE_CONFIRMACION");

        // Con confirmación explícita: procede.
        var r = await Handler(db).Handle(
            new ReintentarTimbradoCommand(fv.Id, ConfirmarNoDuplicado: true), CancellationToken.None);
        r.Estado.Should().Be("Timbrado");
    }

    [Fact]
    public async Task Reintento_con_periodo_cerrado_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId);

        var act = () => Handler(db, periodoAbierto: false)
            .Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("PERIODO_CERRADO");
        fv.Estado.Should().Be(EstadoTimbrado.TimbradoFallido); // no se reabrió
    }

    [Fact]
    public async Task Reintento_de_comprobante_inexistente_es_404()
    {
        using var db = NewDb(Guid.NewGuid());

        var act = () => Handler(db).Handle(new ReintentarTimbradoCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task Reintentos_acumulan_bitacora_de_intentos()
    {
        // [Decisión 01-G] G4: una fila por llamada al PAC, numeradas 1..N.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId);

        await Handler(db, fiscal: new FakeFiscalRechaza("CFDI40148"))
            .Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);
        await Handler(db).Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        var intentos = await db.BitacorasIntentoTimbrado
            .Where(b => b.ComprobanteId == fv.Id)
            .OrderBy(b => b.IntentoNumero)
            .ToListAsync();
        intentos.Should().HaveCount(2);
        intentos[0].IntentoNumero.Should().Be(1);
        intentos[0].Resultado.Should().Be(ResultadoIntentoTimbrado.Fallido);
        intentos[0].ErrorCodigo.Should().Be("CFDI40148");
        intentos[1].IntentoNumero.Should().Be(2);
        intentos[1].Resultado.Should().Be(ResultadoIntentoTimbrado.Timbrado);
    }

    [Fact]
    public async Task Reintento_exitoso_con_pedido_tomado_dispara_writeback_aw()
    {
        // [Decisión 01-G] G5: el pedido ya está Facturado apuntando a esta
        // factura desde la emisión; el write-back A+W debe disparar igual.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var pedido = Millet.Facturacion.Domain.Pedidos.PedidoFacturable.ImportarDesdeAw(
            empresaId, "AW-200", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        db.PedidosFacturables.Add(pedido);
        var control = Millet.Facturacion.Domain.Ingesta.IngestaControl.Crear(
            empresaId, Millet.Facturacion.Domain.Pedidos.OrigenPedido.Aw, "AW-200",
            "hash", 1, Millet.Facturacion.Domain.Ingesta.EstadoIngesta.Importado, pedido.Id, Ahora);
        db.IngestaControles.Add(control);
        db.SaveChanges();

        var fv = CrearFacturaFallidaDePedido(db, empresaId, pedido.Id);
        pedido.MarcarFacturado(fv.Id); // G5: tomado desde el primer intento
        db.SaveChanges();

        var r = await Handler(db).Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        r.Estado.Should().Be("Timbrado");
        pedido.Estado.Should().Be(Millet.Facturacion.Domain.Pedidos.EstadoPedidoFacturable.Facturado);
        pedido.ComprobanteVigenteId.Should().Be(fv.Id);
        control.WriteBackPendiente.Should().BeTrue();
        control.WriteBackEstado.Should().Be("Facturado");
        control.WriteBackUuid.Should().Be(r.Uuid);
    }

    private static FacturaVenta CrearFacturaFallidaDePedido(
        FacturacionDbContext db, Guid empresaId, Guid pedidoId)
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "COTT-2026-000002", 2, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 7,
            (short)1, ComportamientoFiscal.MostradorInmediato, pedidoId, null, null, false);
        fv.AgregarLinea(null, "01010101", "Vidrio", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbradoFallido("CFDI40139", "rechazo CFDI40139");
        db.FacturasVenta.Add(fv);
        db.SaveChanges();
        return fv;
    }

    // ───────────────────────── FSM de dominio ─────────────────────────

    [Fact]
    public void ReabrirParaReintentoTimbrado_regresa_a_borrador_conservando_error()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = CrearFacturaFallida(db, empresaId, "CFDI40139");

        fv.ReabrirParaReintentoTimbrado();

        fv.Estado.Should().Be(EstadoTimbrado.Borrador);
        fv.TimbradoErrorCodigo.Should().Be("CFDI40139"); // trazabilidad hasta que el reintento lo resuelva
    }

    [Fact]
    public void ReabrirParaReintentoTimbrado_rechaza_estados_distintos_de_fallido()
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(Guid.NewGuid(), "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 7,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);

        var act = fv.ReabrirParaReintentoTimbrado;

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("COMPROBANTE_NO_REINTENTABLE");
    }
    [Fact]
    public async Task Receptor_invalido_impide_reintento_sin_PAC_ni_cambio_de_estado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var factura = CrearFacturaFallida(db, empresaId);
        db.Entry(factura).Property(f => f.ReceptorCodigoPostal).CurrentValue = "";
        await db.SaveChangesAsync();
        var pac = new FakeFiscalApiClient();

        var act = () => Handler(db, fiscal: pac).Handle(new ReintentarTimbradoCommand(factura.Id), CancellationToken.None);

        var error = await act.Should().ThrowAsync<Millet.Facturacion.Application.Timbrado.ReceptorFiscalInvalidoException>();
        error.Which.Campos.Should().Contain(c => c.Campo == "codigoPostalFiscal");
        factura.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
        pac.UltimaEmision.Should().BeNull();
        db.BitacorasIntentoTimbrado.Should().BeEmpty();
    }

    // ─────────── 86e3t97tx: reintento sin doble UUID (concurrencia) ───────────

    /// <summary>PAC que cuenta llamadas y ejecuta un callback al ser invocado.</summary>
    private sealed class PacEspia(Func<Task>? alLlamar = null) : ICfdiTimbradoPort
    {
        private readonly FakeFiscalApiClient _inner = new();
        public int Llamadas { get; private set; }
        public async Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct)
        {
            Llamadas++;
            if (alLlamar is not null) await alLlamar();
            return await _inner.TimbrarAsync(emision, ct);
        }
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    /// <summary>Paso posterior al PAC que revienta (simula caída antes del SaveChanges final).</summary>
    private sealed class ContabilidadQueFalla : Millet.Facturacion.Domain.Ports.IContabilidadAsientoPort
    {
        public Task RegistrarAsientoAsync(Millet.Facturacion.Domain.Ports.AsientoContableSolicitud s, CancellationToken ct) =>
            throw new InvalidOperationException("caída simulada después del PAC");
    }

    /// <summary>Periodo abierto que ejecuta un callback (punto entre la carga y el guardado del handler).</summary>
    private sealed class PeriodoConGancho(Func<Task> gancho) : Millet.Facturacion.Domain.Ports.IPeriodoContablePort
    {
        private bool _ejecutado;
        public async Task<bool> EstaAbiertoAsync(int anio, int mes, CancellationToken ct)
        {
            if (!_ejecutado) { _ejecutado = true; await gancho(); }
            return true;
        }
    }

    private static ReintentarTimbradoHandler HandlerCon(
        FacturacionDbContext db, ICfdiTimbradoPort fiscal,
        Millet.Facturacion.Domain.Ports.IContabilidadAsientoPort? contabilidad = null,
        Millet.Facturacion.Domain.Ports.IPeriodoContablePort? periodo = null) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-000001", 1, "")),
            periodo ?? new FakePeriodoContablePort(true), fiscal,
            new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(),
            contabilidad ?? new FakeContabilidadAsientoPort(), new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db));

    [Fact]
    public async Task Reintento_persiste_TimbradoEnProceso_antes_de_llamar_al_PAC()
    {
        var empresaId = Guid.NewGuid();
        var nombreBase = Guid.NewGuid().ToString();
        using var db = NewDb(empresaId, nombreBase);
        var fv = CrearFacturaFallida(db, empresaId, "CFDI40179");
        EstadoTimbrado? estadoPersistidoAlLlamarPac = null;
        var pac = new PacEspia(async () =>
        {
            using var otra = NewDb(empresaId, nombreBase);
            estadoPersistidoAlLlamarPac = (await otra.Comprobantes.AsNoTracking().SingleAsync(c => c.Id == fv.Id)).Estado;
        });

        var r = await HandlerCon(db, pac).Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        r.Estado.Should().Be("Timbrado");
        pac.Llamadas.Should().Be(1);
        // Otro reintento concurrente ya ve el comprobante "en proceso" y no puede llamar al PAC.
        estadoPersistidoAlLlamarPac.Should().Be(EstadoTimbrado.TimbradoEnProceso);
    }

    [Fact]
    public async Task Falla_despues_del_PAC_deja_en_proceso_y_bloquea_reintento_inmediato_sin_PAC()
    {
        var empresaId = Guid.NewGuid();
        var nombreBase = Guid.NewGuid().ToString();
        Guid id;
        using (var db = NewDb(empresaId, nombreBase))
        {
            var fv = CrearFacturaFallida(db, empresaId, "CFDI40179");
            id = fv.Id;
            var act = () => HandlerCon(db, new PacEspia(), contabilidad: new ContabilidadQueFalla())
                .Handle(new ReintentarTimbradoCommand(id), CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        // Lo persistido NO es un TimbradoFallido con código limpio (el PAC pudo haber timbrado).
        using (var lectura = NewDb(empresaId, nombreBase))
        {
            var persistido = await lectura.Comprobantes.AsNoTracking().SingleAsync(c => c.Id == id);
            persistido.Estado.Should().Be(EstadoTimbrado.TimbradoEnProceso);
        }

        // Un reintento inmediato es rechazado sin tocar el PAC.
        using var db2 = NewDb(empresaId, nombreBase);
        var pac2 = new PacEspia();
        var reintento = () => HandlerCon(db2, pac2).Handle(new ReintentarTimbradoCommand(id), CancellationToken.None);
        (await reintento.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("COMPROBANTE_NO_REINTENTABLE");
        pac2.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Reintento_con_version_cambiada_por_otro_responde_REINTENTO_EN_CURSO_sin_PAC()
    {
        var empresaId = Guid.NewGuid();
        var nombreBase = Guid.NewGuid().ToString();
        using var db = NewDb(empresaId, nombreBase);
        var fv = CrearFacturaFallida(db, empresaId, "CFDI40179");
        // Otro reintento gana la carrera entre la carga y el guardado de éste.
        var periodo = new PeriodoConGancho(async () =>
        {
            using var otra = NewDb(empresaId, nombreBase);
            var c = await otra.Comprobantes.SingleAsync(x => x.Id == fv.Id);
            otra.Entry(c).Property(x => x.Version).CurrentValue = c.Version + 1;
            await otra.SaveChangesAsync();
        });
        var pac = new PacEspia();

        var act = () => HandlerCon(db, pac, periodo: periodo)
            .Handle(new ReintentarTimbradoCommand(fv.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be("REINTENTO_EN_CURSO");
        pac.Llamadas.Should().Be(0);
    }
}
