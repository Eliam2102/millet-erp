using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Cancelaciones;
using Millet.Facturacion.Application.Cancelaciones.AutorizarCancelacion;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Pedidos;
using Millet.Facturacion.Application.Timbrado.DescartarComprobante;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Cancelaciones;

/// <summary>
/// M-188 (86e3zr7kw): cancelar ante el SAT la factura de una salida parcial cuando A+W ya canceló el pedido.
/// Antes los efectos llamaban <c>RevertirAFacturable</c> y tronaban con <c>PEDIDO_CANCELADO</c>. Datos ficticios.
/// </summary>
public sealed class CancelacionPedidoCanceladoEnOrigenTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ingresos = Guid.NewGuid();
    private static readonly Guid Contador = Guid.NewGuid();

    [Fact]
    public async Task Criterio1_y_4_pedido_cancelado_en_AW_la_factura_de_la_salida_se_cancela_y_la_salida_queda_descartada()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, primera, _) = await PedidoConDosSalidasAsync(db, empresa);
        var fa = await FacturaDeAsync(db, empresa, pedido, primera);
        pedido.Cancelar(); // A+W cancela el pedido (sigue Importado: solo una parcialidad facturada)
        await db.SaveChangesAsync();

        var resp = await SolicitarYAutorizarAsync(db, empresa, fa.Id);

        resp.EstadoComprobante.Should().Be("Cancelado");
        (await db.FacturasVenta.SingleAsync(f => f.Id == fa.Id)).Estado.Should().Be(EstadoTimbrado.Cancelado);
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Cancelado);
        var salida = await db.SalidasFacturables.SingleAsync(s => s.Id == primera.Id);
        salida.Descartada.Should().BeTrue();
        salida.MotivoDescarte.Should().Be(EfectosCancelacionPedido.MotivoSalidaPedidoCancelado);
    }

    [Fact]
    public async Task Criterio2_pedido_vigente_la_salida_queda_libre_y_el_pedido_no_cambia_de_estado()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, primera, _) = await PedidoConDosSalidasAsync(db, empresa);
        var fa = await FacturaDeAsync(db, empresa, pedido, primera);

        await SolicitarYAutorizarAsync(db, empresa, fa.Id);

        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Importado);
        (await db.SalidasFacturables.SingleAsync(s => s.Id == primera.Id)).Descartada.Should().BeFalse();
        // Sin factura vigente, la salida se puede volver a facturar.
        (await db.FacturasVenta.AnyAsync(f => f.SalidaFacturableId == primera.Id
            && (f.Estado == EstadoTimbrado.Timbrado || f.Estado == EstadoTimbrado.CancelacionPendiente))).Should().BeFalse();
    }

    [Fact]
    public async Task Criterio3_pedido_completo_cancelado_en_AW_cancela_la_factura_y_deja_nota_en_excepciones()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = Pedido(empresa);
        db.PedidosFacturables.Add(pedido);
        var fa = Factura(empresa, pedido.Id, "FA-000188");
        db.FacturasVenta.Add(fa);
        await db.SaveChangesAsync();
        pedido.Cancelar();
        await db.SaveChangesAsync();

        var resp = await SolicitarYAutorizarAsync(db, empresa, fa.Id);

        resp.EstadoComprobante.Should().Be("Cancelado");
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Cancelado);
        var nota = await db.ExcepcionesImportacion.SingleAsync();
        nota.PedidoRef.Should().Be("10425188");
        nota.Detalle.Should().Contain("Factura cancelada de un pedido cancelado en origen").And.Contain("FA-000188");
        nota.Resuelto.Should().BeFalse();
    }

    [Fact]
    public async Task Regresion_M8_cancelar_una_parcialidad_de_un_pedido_facturado_lo_regresa_a_importado()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, primera, segunda) = await PedidoConDosSalidasAsync(db, empresa);
        var fa1 = await FacturaDeAsync(db, empresa, pedido, primera);
        var fa2 = await FacturaDeAsync(db, empresa, pedido, segunda);
        (await PedidoPorParcialidades.TomarPedidoSiCompletoAsync(db, fa2, pedido, default)).Should().BeTrue();
        await db.SaveChangesAsync();

        await SolicitarYAutorizarAsync(db, empresa, fa1.Id);

        var despues = await db.PedidosFacturables.SingleAsync();
        despues.Estado.Should().Be(EstadoPedidoFacturable.Importado);
        despues.ComprobanteVigenteId.Should().BeNull();
    }

    [Fact]
    public async Task Poller_pedido_cancelado_en_AW_resuelve_la_cancelacion_sin_error_y_descarta_la_salida()
    {
        var empresa = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresa));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<ICfdiTimbradoPort>(new FakeFiscalNoVigente());
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddSingleton<IIntegrationEventPublisher>(new FakeIntegrationEventPublisher());
        services.AddOptions<CancelacionSatPollerOptions>();
        var sp = services.BuildServiceProvider();

        Guid salidaId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var (pedido, primera, _) = await PedidoConDosSalidasAsync(db, empresa);
            var fa = await FacturaDeAsync(db, empresa, pedido, primera);
            fa.MarcarCancelacionPendiente();
            pedido.Cancelar();
            var solicitud = SolicitudCancelacion.Crear(empresa, fa.Id, "02", null, Ahora);
            solicitud.Autorizar(Contador, "Contador", Ahora);
            solicitud.MarcarEnProceso("EnProceso");
            db.SolicitudesCancelacion.Add(solicitud);
            await db.SaveChangesAsync();
            salidaId = primera.Id;
        }

        var worker = new CancelacionSatPollerWorker(sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<CancelacionSatPollerOptions>>(), NullLogger<CancelacionSatPollerWorker>.Instance);

        (await worker.TickAsync(CancellationToken.None)).Should().Be(1);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            (await db.SolicitudesCancelacion.SingleAsync()).Estado.Should().Be(EstadoSolicitudCancelacion.Aceptada);
            (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Cancelado);
            (await db.SalidasFacturables.SingleAsync(s => s.Id == salidaId)).Descartada.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Descartar_la_factura_fallida_de_una_salida_de_pedido_cancelado_descarta_la_salida()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (pedido, primera, _) = await PedidoConDosSalidasAsync(db, empresa);
        var fa = await FacturaDeAsync(db, empresa, pedido, primera, timbrar: false);
        fa.MarcarTimbradoEnProceso();
        fa.MarcarTimbradoFallido("CFDI40147", "RFC del receptor no válido");
        pedido.Cancelar();
        await db.SaveChangesAsync();

        var resp = await new DescartarComprobanteHandler(db, new FakeClock(Ahora))
            .Handle(new DescartarComprobanteCommand(fa.Id), CancellationToken.None);

        resp.PedidoLiberadoId.Should().BeNull();
        (await db.SalidasFacturables.SingleAsync(s => s.Id == primera.Id)).Descartada.Should().BeTrue();
        (await db.PedidosFacturables.SingleAsync()).Estado.Should().Be(EstadoPedidoFacturable.Cancelado);
    }

    // ---- Andamiaje ----

    private static async Task<SolicitarCancelacionResponse> SolicitarYAutorizarAsync(FacturacionDbContext db, Guid empresa, Guid comprobanteId)
    {
        await new SolicitarCancelacionHandler(db, new FakeEmpresaContext(empresa), new FakeUserContext(Ingresos, "Ingresos"), new FakeClock(Ahora))
            .Handle(new SolicitarCancelacionCommand(comprobanteId, "02", null), CancellationToken.None);
        return await new AutorizarCancelacionHandler(db, new FakeFiscalApiClient(), new FakeIntegrationEventPublisher(),
                new FakeUserContext(Contador, "Contador"), new FakeClock(Ahora))
            .Handle(new AutorizarCancelacionCommand(comprobanteId), CancellationToken.None);
    }

    private static PedidoFacturable Pedido(Guid empresa)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresa, "10425188", Guid.NewGuid(), Guid.NewGuid(), "Cliente DEMO",
            (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.AgregarLinea(Guid.NewGuid(), "Vidrio DEMO A", "30171710", "H87", 10m, 100m, 0m, false);
        pedido.AgregarLinea(Guid.NewGuid(), "Vidrio DEMO B", "30171710", "H87", 10m, 50m, 0m, false);
        pedido.RecalcularTotal();
        return pedido;
    }

    private static async Task<(PedidoFacturable, SalidaFacturable, SalidaFacturable)> PedidoConDosSalidasAsync(FacturacionDbContext db, Guid empresa)
    {
        var pedido = Pedido(empresa);
        db.PedidosFacturables.Add(pedido);
        var primera = SalidaFacturable.Registrar(pedido, [], OrigenSalidaFacturable.Manual, "SAL-1", [(1, 6m), (2, 6m)], Ahora);
        var segunda = SalidaFacturable.Registrar(pedido, [primera], OrigenSalidaFacturable.Manual, "SAL-2", [(1, 4m), (2, 4m)], Ahora);
        db.SalidasFacturables.AddRange(primera, segunda);
        await db.SaveChangesAsync();
        return (pedido, primera, segunda);
    }

    private static async Task<FacturaVenta> FacturaDeAsync(
        FacturacionDbContext db, Guid empresa, PedidoFacturable pedido, SalidaFacturable salida, bool timbrar = true)
    {
        var factura = FacturaVenta.CrearBorrador(empresa, $"FA-{salida.NumeroParcialidad:000000}", salida.NumeroParcialidad, Guid.NewGuid(), null, null,
            Receptor(), Emisor(), "PUE", "03", "MXN", null, 2026, 11, (short)1, ComportamientoFiscal.MostradorInmediato, pedido.Id, null, null, false);
        foreach (var ls in salida.Lineas.OrderBy(l => l.Posicion))
        {
            var lp = pedido.Lineas.Single(l => l.Posicion == ls.Posicion);
            factura.AgregarLinea(lp.ProductoId, "30171710", lp.ProductoDescripcion, "H87", ls.Cantidad, lp.Precio, 0m, "02", 0.16m, null, null);
        }
        factura.RecalcularTotales();
        factura.LigarSalida(salida.Id, pedido.Id);
        if (timbrar)
        {
            factura.MarcarTimbradoEnProceso();
            factura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        }
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();
        return factura;
    }

    private static FacturaVenta Factura(Guid empresa, Guid pedidoId, string folio)
    {
        var fv = FacturaVenta.CrearBorrador(empresa, folio, 188, Guid.NewGuid(), null, null, Receptor(), Emisor(),
            "PUE", "03", "MXN", null, 2026, 11, (short)1, ComportamientoFiscal.MostradorInmediato, pedidoId, null, null, false);
        fv.AgregarLinea(null, "30171710", "Vidrio DEMO", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return fv;
    }

    private static DatosFiscalesReceptor Receptor() => new("AAA010101AAA", "Cliente DEMO", "601", "97000", "G03", "MEX", false);
    private static DatosFiscalesEmisor Emisor() => new("BBB010101BBB", "Emisor DEMO", "601", "77524");

    private static FacturacionDbContext NewDb(Guid empresa) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresa));

    private sealed class FakeFiscalNoVigente : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => throw new NotImplementedException();
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) => throw new NotImplementedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) =>
            Task.FromResult(new EstatusCfdiResultado(false, "Cancelado", false, "Cancelado"));
    }
}
