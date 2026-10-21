using MediatR;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.Facturacion.Domain.Pedidos;
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
        ReceptorCodigoPostal: "76120",
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
        Millet.Integraciones.Fiscal.Domain.Ports.ICfdiTimbradoPort? fiscal = null, IEmbarqueExportacionReadPort? embarques = null, string receptorRfc = "XAXX010101000",
        FakeSender? sender = null) =>
        new(
            db,
            sender ?? new FakeSender(new ReservarFolioResponse("FA-000001", 1, "")),
            new FakePeriodoContablePort(periodoAbierto),
            new FakeCatalogosSatReadPort(catalogosValidos),
            fiscal ?? new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(),
            new FakeEmpresaContext(empresaId),
            new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db, receptorRfc, "616", "76120"), embarques: embarques);

    private sealed class Embarques(EmbarqueExportacionLectura lectura) : IEmbarqueExportacionReadPort
    {
        public Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, CancellationToken ct) => Task.FromResult(lectura);
    }
    private static PedidoFacturable CrearPedido(FacturacionDbContext db, Guid empresaId, Guid sucursal, ComportamientoFiscal comportamiento = ComportamientoFiscal.ExportacionConCce)
    {
        var p = PedidoFacturable.ImportarDesdeAw(empresaId, "DEMO-PED", sucursal, Guid.NewGuid(), "DEMO cliente", 1,
            comportamiento, "MXN", null, null, null, 1, "15");
        p.AgregarLinea(null, "Producto", "01010101", "H87", 2, 100, 0, false); p.RecalcularTotal();
        db.PedidosFacturables.Add(p); db.SaveChanges(); return p;
    }
    [Fact]
    public async Task Pedido_no_ligado_se_factura_igual_y_sin_copia_ce()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa); var command = Command(); var p = CrearPedido(db, empresa, command.SucursalId);
        var resp = await Handler(db, empresa, embarques: new Embarques(new(false))).Handle(command with { PedidoFacturableId = p.Id }, CancellationToken.None);
        Assert.Equal("Timbrado", resp.Estado); Assert.Null((await db.FacturasVenta.SingleAsync()).DatosEmbarqueCe);
        Assert.Equal(EstadoPedidoFacturable.Facturado, p.Estado);
    }
    [Theory]
    [InlineData("FAC_PEDIDO_NO_EMBARCADO")][InlineData("CE_PARTIDA_REQUIERE_REVISION")]
    public async Task Pedido_ligado_incompleto_rechaza_antes_de_folio_y_pac(string codigo)
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Handler(db, empresa, embarques: new Embarques(new(true, CodigoBloqueo: codigo, Motivo: "DEMO falta embarque")))
            .Handle(Command() with { PedidoFacturableId = Guid.NewGuid() }, CancellationToken.None));
        Assert.Equal(codigo, ex.Code); Assert.Empty(db.FacturasVenta);
    }
    [Fact]
    public async Task RT_no_reinterpreta_exportacion_con_cce_aunque_comando_pida_administrativa()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa); var command = Command(); var p = CrearPedido(db, empresa, command.SucursalId);
        var lectura = new EmbarqueExportacionLectura(true, Guid.NewGuid(), command.SucursalId, true, FolioExpediente: "CE-EXP-DEMO",
            NumeroPedimento: "DEMO-RT", ClavePedimento: "RT", FechaPedimento: new(2026, 5, 29), Incoterm: "DDP", Contenedores: []);
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Handler(db, empresa, embarques: new Embarques(lectura))
            .Handle(command with { PedidoFacturableId = p.Id, ComportamientoFiscal = ComportamientoFiscal.Administrativa }, CancellationToken.None));
        Assert.Equal("FAC_RT_POR_CONFIRMAR", ex.Code); Assert.Empty(db.FacturasVenta);
    }
    [Fact]
    public async Task RT_copia_embarque_ignora_cce_y_conserva_informacion_aduanera_de_linea()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa); var command = Command(); var p = CrearPedido(db, empresa, command.SucursalId, command.ComportamientoFiscal);
        var lectura = new EmbarqueExportacionLectura(true, Guid.NewGuid(), command.SucursalId, true, FolioExpediente: "CE-EXP-DEMO",
            NumeroPedimento: "DEMO-RT", ClavePedimento: "RT", FechaPedimento: new(2026, 5, 29), Incoterm: "DDP", Contenedores: [new(Guid.NewGuid(), "DEMO-C", "DEMO-BL", new(2026, 5, 30))]);
        var cce = new EmitirFacturaVentaCce("2", "FOB", 20, "DEMO-TAX", "USA", [], "A1");
        await Handler(db, empresa, embarques: new Embarques(lectura)).Handle(command with { PedidoFacturableId = p.Id, Cce = cce }, CancellationToken.None);
        var f = await db.FacturasVenta.Include(f => f.Lineas).SingleAsync();
        Assert.Equal("RT", f.DatosEmbarqueCe!.ClavePedimento); Assert.Equal("DEMO-BL", f.DatosEmbarqueCe.Contenedores.Single().BlGuia);
        Assert.Equal(command.ComportamientoFiscal, f.ComportamientoFiscal);
        Assert.Null(f.ComplementoCce); Assert.All(f.Lineas, l => Assert.Null(l.Pedimento));
        Assert.Equal("FAC_EMBARQUE_INMUTABLE", Assert.Throws<BusinessRuleException>(() => f.TomarEmbarqueCe(f.DatosEmbarqueCe)).Code);
    }

    [Fact]
    public async Task CFDI_cancelado_permite_refacturar_con_la_copia_vigente_sin_perder_el_pedido()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa); var command = Command(); var p = CrearPedido(db, empresa, command.SucursalId, command.ComportamientoFiscal);
        var lectura = new EmbarqueExportacionLectura(true, Guid.NewGuid(), command.SucursalId, true, FolioExpediente: "CE-EXP-DEMO",
            NumeroPedimento: "DEMO-RT", ClavePedimento: "RT", FechaPedimento: new(2026, 5, 29), Incoterm: "DDP", Contenedores: [new(Guid.NewGuid(), "DEMO-C", "DEMO-BL", new(2026, 5, 30))]);
        command = command with { PedidoFacturableId = p.Id };
        var handler = Handler(db, empresa, embarques: new Embarques(lectura));
        await handler.Handle(command, CancellationToken.None);
        var anterior = await db.FacturasVenta.SingleAsync(); anterior.MarcarCancelacionPendiente(); anterior.MarcarCancelado(); p.RevertirAFacturable(); await db.SaveChangesAsync();
        await handler.Handle(command, CancellationToken.None);
        var nueva = await db.FacturasVenta.SingleAsync(f => f.Id != anterior.Id);
        Assert.Equal(EstadoPedidoFacturable.Facturado, p.Estado); Assert.Equal(nueva.Id, p.ComprobanteVigenteId);
        Assert.Equal(anterior.DatosEmbarqueCe!.NumeroPedimento, nueva.DatosEmbarqueCe!.NumeroPedimento);
    }

    [Fact]
    public async Task A1_toma_clave_e_incoterm_del_expediente_y_no_del_usuario()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa); var command = Command(); var p = CrearPedido(db, empresa, command.SucursalId);
        var lectura = new EmbarqueExportacionLectura(true, Guid.NewGuid(), command.SucursalId, true, FolioExpediente: "CE-EXP-DEMO",
            NumeroPedimento: "DEMO-A1", ClavePedimento: "A1", FechaPedimento: new(2026, 5, 29), Incoterm: "DDP", Contenedores: [new(Guid.NewGuid(), "DEMO-C", "DEMO-BL", new(2026, 5, 30))]);
        var cce = new EmitirFacturaVentaCce("2", "FOB", 20, "DEMO-TAX", "USA", [new("70071999", "06", 2, 100, 200, true)], "RT", ReceptorDomicilioEstado: "DEMO", ReceptorDomicilioCodigoPostal: "00000");
        await Handler(db, empresa, embarques: new Embarques(lectura), receptorRfc: "XEXX010101000").Handle(command with {
            PedidoFacturableId = p.Id, Cce = cce, ReceptorRfc = "XEXX010101000", ReceptorPais = "USA" }, CancellationToken.None);
        var f = await db.FacturasVenta.Include(f => f.ComplementoCce).SingleAsync();
        Assert.Equal("A1", f.ComplementoCce!.ClaveDePedimento); Assert.Equal("DDP", f.ComplementoCce.Incoterm);
        Assert.Equal("DEMO-A1", f.DatosEmbarqueCe!.NumeroPedimento);
    }

    [Fact]
    public async Task Handle_emite_factura_timbrada_con_uuid_fake()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var resp = await Handler(db, empresaId).Handle(Command(), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrEmpty();
        resp.TimbradoErrorCodigo.Should().BeNull();
        resp.TimbradoErrorMensaje.Should().BeNull();
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
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, Guid.NewGuid(), Millet.Facturacion.Domain.Pedidos.ClasePedidoManual.VentaDirecta, Millet.Facturacion.Domain.Pedidos.SubtipoVentaDirecta.Servicio, null, null);
        db.PedidosFacturables.Add(pedido);
        db.SaveChanges();

        var resp = await Handler(db, empresaId, fiscal: new FakeFiscalRechaza())
            .Handle(Command() with { PedidoFacturableId = pedido.Id }, CancellationToken.None);

        resp.Estado.Should().Be("TimbradoFallido");
        resp.Uuid.Should().BeNull();
        resp.TimbradoErrorCodigo.Should().Be("CFDI40139");
        resp.TimbradoErrorMensaje.Should().Be("rechazo CFDI40139");
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
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(db, "XAXX010101000", "616", "76120"));

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "EMPRESA_NO_SELECCIONADA");
    }

    // ----- F6.1: venta directa con serie propia; no se informa a A+W -----

    private static PedidoFacturable PedidoManual(FacturacionDbContext db, Guid empresaId, ClasePedidoManual? clase,
        SubtipoVentaDirecta? subtipo = null, ComportamientoFiscal comportamiento = ComportamientoFiscal.MostradorInmediato)
    {
        var p = PedidoFacturable.CrearManual(empresaId, "VD-1", Guid.NewGuid(), Guid.NewGuid(), "Cliente", 1,
            comportamiento, "MXN", null, null, null, Guid.NewGuid(),
            clase ?? ClasePedidoManual.VentaDirecta, clase is null ? SubtipoVentaDirecta.Servicio : subtipo,
            clase == ClasePedidoManual.Emergencia ? "A+W fuera de servicio" : null, null);
        if (clase is null)
            Millet.Facturacion.UnitTests.Pedidos.PedidoFacturableManualClasificacionTests.VolverLegacy(p);
        db.PedidosFacturables.Add(p);
        db.SaveChanges();
        return p;
    }

    [Theory]
    [InlineData(ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio)]
    [InlineData(ClasePedidoManual.Emergencia, null)]
    public async Task Pedido_manual_clasificado_usa_serie_VentaDirecta_sin_writeback_AW(
        ClasePedidoManual clase, SubtipoVentaDirecta? subtipo)
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa);
        var pedido = PedidoManual(db, empresa, clase, subtipo);
        var sender = new FakeSender(new ReservarFolioResponse("VD-MID-000001", 1, ""));

        var resp = await Handler(db, empresa, sender: sender)
            .Handle(Command() with { PedidoFacturableId = pedido.Id }, CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Folio.Should().Be("VD-MID-000001");
        sender.TiposReservados.Should().Equal(Millet.Administracion.Domain.TipoDocumentoSerie.VentaDirecta);
        pedido.Estado.Should().Be(EstadoPedidoFacturable.Facturado);
        db.IngestaControles.Should().BeEmpty("la venta directa no se informa a A+W");
    }

    [Fact]
    public async Task Pedido_AW_sigue_usando_serie_Cfdi()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa); var command = Command();
        var p = CrearPedido(db, empresa, command.SucursalId, ComportamientoFiscal.MostradorInmediato);
        var sender = new FakeSender(new ReservarFolioResponse("F-000001", 1, ""));

        await Handler(db, empresa, sender: sender).Handle(command with { PedidoFacturableId = p.Id }, CancellationToken.None);

        sender.TiposReservados.Should().Equal(Millet.Administracion.Domain.TipoDocumentoSerie.Cfdi);
    }

    [Fact]
    public async Task Pedido_manual_legacy_sin_clase_se_rechaza_sin_quemar_folio()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa);
        var pedido = PedidoManual(db, empresa, clase: null);
        var sender = new FakeSender(new ReservarFolioResponse("VD-000001", 1, ""));

        var act = () => Handler(db, empresa, sender: sender)
            .Handle(Command() with { PedidoFacturableId = pedido.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PEDIDO_MANUAL_SIN_CLASIFICAR");
        sender.VecesReservoFolio.Should().Be(0);
        db.FacturasVenta.Should().BeEmpty();
    }

    [Fact]
    public async Task Venta_directa_activo_exige_comportamiento_VentaActivoFijo()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa);
        var pedido = PedidoManual(db, empresa, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo, ComportamientoFiscal.VentaActivoFijo);

        var act = () => Handler(db, empresa)
            .Handle(Command() with { PedidoFacturableId = pedido.Id }, CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "FAC_PEDIDO_ACTIVO_COMPORTAMIENTO");
    }

    [Fact]
    public async Task Venta_directa_activo_sin_autorizacion_no_se_timbra_CA6_12()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa);
        var pedido = PedidoManual(db, empresa, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo, ComportamientoFiscal.VentaActivoFijo);
        var sender = new FakeSender(new ReservarFolioResponse("VD-000001", 1, ""));

        var act = () => Handler(db, empresa, sender: sender).Handle(Command() with
        {
            PedidoFacturableId = pedido.Id, ComportamientoFiscal = ComportamientoFiscal.VentaActivoFijo,
        }, CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AUTORIZACION_REQUERIDA");
        sender.VecesReservoFolio.Should().Be(0);
    }

    [Fact]
    public async Task Venta_directa_activo_con_autorizacion_la_consume_y_usa_serie_VD()
    {
        var empresa = Guid.NewGuid(); using var db = NewDb(empresa);
        var pedido = PedidoManual(db, empresa, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo, ComportamientoFiscal.VentaActivoFijo);
        var autorizacion = Millet.Facturacion.Domain.Activos.AutorizacionVentaActivo.Crear(
            empresa, "DEMO-ACT", "Camioneta", 250000m, 180000m, false, 200m, Guid.NewGuid(), Ahora); // AF-04: precio autorizado = neto de la factura (2 × $100)
        db.AutorizacionesVentaActivo.Add(autorizacion); db.SaveChanges();
        var sender = new FakeSender(new ReservarFolioResponse("VD-000001", 1, ""));

        var resp = await Handler(db, empresa, sender: sender).Handle(Command() with
        {
            PedidoFacturableId = pedido.Id, ComportamientoFiscal = ComportamientoFiscal.VentaActivoFijo,
            AutorizacionId = autorizacion.Id,
        }, CancellationToken.None);

        sender.TiposReservados.Should().Equal(Millet.Administracion.Domain.TipoDocumentoSerie.VentaDirecta);
        autorizacion.Estado.Should().Be(Millet.Facturacion.Domain.Activos.EstadoAutorizacionActivo.Usada);
        autorizacion.FacturaVentaId.Should().Be(resp.Id);
    }
}
