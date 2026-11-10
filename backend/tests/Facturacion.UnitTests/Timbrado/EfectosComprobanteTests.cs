using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Application.Timbrado.Efectos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;
using Xunit;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>M-285 (86e3zr7af): efectos posteriores al timbrado registrados y reintentables. Datos ficticios.</summary>
public sealed class EfectosComprobanteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 22, 0, 0, TimeSpan.Zero);
    private readonly Guid _empresa = Guid.NewGuid();

    /// <summary>Publicador que falla mientras <see cref="Fallar"/> sea true.</summary>
    private sealed class PublicadorQueFalla : IIntegrationEventPublisher
    {
        public bool Fallar { get; set; } = true;
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            if (Fallar) throw new InvalidOperationException("Outbox no disponible (simulado).");
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(_empresa));

    private async Task<FacturaVenta> FacturaTimbradaAsync(FacturacionDbContext db, PedidoFacturable? pedido = null)
    {
        var f = FacturaVenta.CrearBorrador(_empresa, "MERA-15240", 15240, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"),
            "PUE", "03", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, pedido?.Id, null, null, false);
        f.AgregarLinea(null, "30171500", "Vidrio", "H87", 1m, 10000m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(f);
        await db.SaveChangesAsync();
        return f;
    }

    private static EfectosPostTimbrado Efectos(FacturacionDbContext db, IIntegrationEventPublisher eventos) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-1", 1, "")), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), eventos, ReceptorFiscalTestFactory.Crear(db), null);

    private static ReintentarEfectosComprobanteHandler Reintento(FacturacionDbContext db, IIntegrationEventPublisher eventos) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-1", 1, "")), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), eventos, ReceptorFiscalTestFactory.Crear(db), new FakeClock(Ahora));

    [Fact]
    public async Task Publicador_que_falla_deja_la_factura_timbrada_y_la_poliza_pendiente_con_el_error()
    {
        using var db = Db();
        var f = await FacturaTimbradaAsync(db);
        var eventos = new PublicadorQueFalla();
        var efectos = Efectos(db, eventos);

        await efectos.RegistrarAsync(f, clienteId: Guid.NewGuid(), default);
        await db.SaveChangesAsync();
        var pendientes = await efectos.EjecutarPendientesAsync(f, Ahora, default);

        db.ChangeTracker.Clear();
        (await db.FacturasVenta.SingleAsync()).Estado.Should().Be(EstadoTimbrado.Timbrado);
        var efecto = pendientes.Should().ContainSingle().Which;
        (efecto.Tipo, efecto.Estado, efecto.Intentos).Should().Be((TipoEfectoComprobante.EventoContable, EstadoEfectoComprobante.Pendiente, 1));
        efecto.UltimoError.Should().Contain("Outbox no disponible");
        var detalle = await new EfectosComprobanteHandler(db).Handle(new EfectosComprobanteQuery(f.Id), default);
        detalle.Single().Descripcion.Should().Be("Póliza y cartera (evento de timbrado)");
    }

    [Fact]
    public async Task Reintentar_pendientes_publica_una_vez_y_un_segundo_reintento_no_publica_nada()
    {
        using var db = Db();
        var f = await FacturaTimbradaAsync(db);
        var eventos = new PublicadorQueFalla();
        var efectos = Efectos(db, eventos);
        await efectos.RegistrarAsync(f, clienteId: null, default);
        await db.SaveChangesAsync();
        await efectos.EjecutarPendientesAsync(f, Ahora, default);
        db.ChangeTracker.Clear();

        eventos.Fallar = false;
        var r1 = await Reintento(db, eventos).Handle(new ReintentarEfectosComprobanteCommand(f.Id), default);
        db.ChangeTracker.Clear();
        var r2 = await Reintento(db, eventos).Handle(new ReintentarEfectosComprobanteCommand(f.Id), default);

        r1.Single().Estado.Should().Be("Aplicado");
        r2.Single().Estado.Should().Be("Aplicado");
        eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Con_el_aviso_a_AW_pendiente_solo_se_aplica_ese_y_no_se_vuelve_a_publicar_el_evento()
    {
        using var db = Db();
        var pedido = PedidoFacturable.ImportarDesdeAw(_empresa, "10425200", Guid.NewGuid(), Guid.NewGuid(), "Cliente",
            (short)1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        pedido.AgregarLinea(null, "Vidrio", "30171500", "H87", 1m, 10000m, 0m, false);
        pedido.RecalcularTotal();
        db.PedidosFacturables.Add(pedido);
        await db.SaveChangesAsync();
        var f = await FacturaTimbradaAsync(db, pedido);
        var control = IngestaControlDe(pedido);
        db.IngestaControles.Add(control);
        var evento = EfectoComprobante.Pendiente(_empresa, f.Id, TipoEfectoComprobante.EventoContable);
        evento.MarcarAplicado(Ahora);
        db.EfectosComprobante.AddRange(evento,
            EfectoComprobante.Pendiente(_empresa, f.Id, TipoEfectoComprobante.EstadoPedido));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var eventos = new PublicadorQueFalla { Fallar = false };

        var r = await Reintento(db, eventos).Handle(new ReintentarEfectosComprobanteCommand(f.Id), default);

        r.Should().OnlyContain(e => e.Estado == "Aplicado");
        eventos.Publicados.Should().BeEmpty();
        db.ChangeTracker.Clear();
        var guardado = await db.PedidosFacturables.SingleAsync();
        (guardado.Estado, guardado.ComprobanteVigenteId).Should().Be((EstadoPedidoFacturable.Facturado, f.Id));
        (await db.IngestaControles.SingleAsync()).Estado.Should().Be(EstadoIngesta.Facturado);
    }

    [Fact]
    public async Task La_bandeja_lista_las_facturas_con_efectos_pendientes()
    {
        using var db = Db();
        var f = await FacturaTimbradaAsync(db);
        var efectos = Efectos(db, new PublicadorQueFalla());
        await efectos.RegistrarAsync(f, null, default);
        await db.SaveChangesAsync();
        await efectos.EjecutarPendientesAsync(f, Ahora, default);

        var lista = await new EfectosComprobanteHandler(db).Handle(new ComprobantesConEfectosPendientesQuery(), default);

        lista.Should().ContainSingle().Which.Folio.Should().Be("MERA-15240");
    }

    [Fact]
    public async Task Solo_se_reintentan_efectos_de_un_comprobante_timbrado()
    {
        using var db = Db();
        var f = FacturaVenta.CrearBorrador(_empresa, "MERA-15241", 15241, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"),
            "PUE", "03", "MXN", null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "30171500", "Vidrio", "H87", 1m, 10000m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        db.FacturasVenta.Add(f);
        await db.SaveChangesAsync();

        var act = () => Reintento(db, new PublicadorQueFalla()).Handle(new ReintentarEfectosComprobanteCommand(f.Id), default);

        (await act.Should().ThrowAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>())
            .Which.Code.Should().Be("EFECTOS_SIN_TIMBRE");
    }

    private IngestaControl IngestaControlDe(PedidoFacturable pedido) =>
        IngestaControl.Crear(_empresa, OrigenPedido.Aw, pedido.NumeroPedido!, "hash", 1, EstadoIngesta.Importado, pedido.Id, Ahora);
}
