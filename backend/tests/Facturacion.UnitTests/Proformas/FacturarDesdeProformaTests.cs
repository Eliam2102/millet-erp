using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Proformas;
using Millet.Facturacion.Application.Proformas.EmitirFacturaDesdeProforma;
using Millet.Facturacion.Application.Proformas.GenerarProformaPedido;
using Millet.Facturacion.Application.Proformas.RegistrarConformidadProforma;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Proformas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Proformas;

/// <summary>
/// M-340: facturar desde la versión aprobada de la proforma, sin recaptura. La factura coincide en importes con la proforma y
/// muestra de cuál viene; si el pedido cambió después de aprobar no se emite y la conformidad queda invalidada; sin conformidad
/// o con la conformidad invalidada no se factura. Datos genéricos.
/// </summary>
public sealed class FacturarDesdeProformaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);
    private static readonly EmpresaFiscalLectura Emisor = new(Guid.Empty, "MIL010101AAA", "Millet", "601", 0.16m, "76120");

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static PedidoFacturable CrearPedido(FacturacionDbContext db, Guid empresa)
    {
        var p = PedidoFacturable.ImportarDesdeAw(empresa, "PED-4001", Guid.NewGuid(), Guid.NewGuid(), "Cliente prueba", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        p.AgregarLinea(null, "Vidrio templado 6mm", "30171500", "H87", 3m, 33.335m, 10m, false, tasaIva: 0.16m);
        p.AgregarLinea(null, "Instalación", "72101500", "E48", 1m, 1000m, 0m, false);
        p.RecalcularTotal();
        db.PedidosFacturables.Add(p);
        db.SaveChanges();
        return p;
    }

    /// <summary>Detalle del pedido tal como lo lee la emisión; <paramref name="cantidad"/> simula un cambio posterior.</summary>
    private static PedidoFacturableDetalleResponse Detalle(PedidoFacturable p, decimal cantidad = 3m, string? estado = null) => new(
        p.Id, p.NumeroPedido, p.Origen.ToString(), estado ?? p.Estado.ToString(), p.SucursalId, p.ClienteId, p.ClienteNombre,
        p.CanalVentaId, "Mostrador", p.ComportamientoFiscal.ToString(), p.Moneda, p.ObraId, p.ObraNombre, null, null,
        p.Total, null, p.Version,
        new PedidoClienteFiscal("AAA010101AAA", "601", "97000", "G03", "01", "PUE", false),
        [
            new PedidoFacturableLineaDetalle(1, null, "Vidrio templado 6mm", "30171500", "H87", cantidad, 33.335m, 10m,
                false, "02", 0.16m, 0.106667m, 0.10m),
            new PedidoFacturableLineaDetalle(2, null, "Instalación", "72101500", "E48", 1m, 1000m, 0m,
                false, null, null, null, null),
        ]);

    /// <summary>Resuelve el detalle del pedido (que puede cambiar) y deja pasar la emisión por el handler real.</summary>
    private sealed class SenderEmision(FacturacionDbContext db, Guid empresa, Func<PedidoFacturableDetalleResponse> detalle, FakeFiscalApiClient pac) : ISender
    {
        public List<EmitirFacturaVentaCommand> Emisiones { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PedidoFacturableDetalleQuery)
                return Task.FromResult((TResponse)(object)detalle());
            if (request is EmitirFacturaVentaCommand emision)
            {
                Emisiones.Add(emision);
                var handler = new EmitirFacturaVentaHandler(db, new FakeSender(new ReservarFolioResponse("FA-000007", 7, "")),
                    new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), pac, new FakeCfdiRepositorioPort(),
                    new FakeEmpresaFiscalReadPort(Emisor with { EmpresaId = empresa }), new FakeIntegrationEventPublisher(),
                    new FakeEmpresaContext(empresa), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora),
                    ReceptorFiscalTestFactory.Crear(db));
                return Task.FromResult((TResponse)(object)handler.Handle(emision, cancellationToken).GetAwaiter().GetResult());
            }
            throw new InvalidOperationException($"No se espera {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static EmitirFacturaDesdeProformaHandler Facturar(FacturacionDbContext db, Guid empresa, ISender sender) =>
        new(db, sender, new FakeEmpresaFiscalReadPort(Emisor with { EmpresaId = empresa }), new FakeEmpresaContext(empresa), new FakeClock(Ahora));

    private static async Task<ProformaPedidoResponse> GenerarProforma(FacturacionDbContext db, Guid empresa, ISender sender, Guid pedidoId) =>
        await new GenerarProformaPedidoHandler(db, sender, new FakeEmpresaFiscalReadPort(Emisor with { EmpresaId = empresa }),
                new FakeEmpresaContext(empresa), new FakeUserContext(Guid.NewGuid(), "Cajera Prueba"), new FakeClock(Ahora))
            .Handle(new GenerarProformaPedidoCommand(pedidoId), CancellationToken.None);

    private static Task<ProformaConformidadResponse> Aprobar(FacturacionDbContext db, Guid pedidoId, Guid proformaId) =>
        new RegistrarConformidadProformaHandler(db, new FakeAlcanceCajaEvaluator(), new FakeUserContext(Guid.NewGuid(), "Cajera Prueba"), new FakeClock(Ahora))
            .Handle(new RegistrarConformidadProformaCommand(pedidoId, proformaId, "Contacto Prueba", MedioConformidadProforma.Correo), CancellationToken.None);

    private static (Guid Empresa, FacturacionDbContext Db, PedidoFacturable Pedido, SenderEmision Sender, FakeFiscalApiClient Pac, Func<decimal> Cantidad, Action<decimal> Cambiar)
        Escenario()
    {
        var empresa = Guid.NewGuid();
        var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa);
        var pac = new FakeFiscalApiClient();
        var cantidad = 3m;
        var sender = new SenderEmision(db, empresa, () => Detalle(pedido, cantidad), pac);
        return (empresa, db, pedido, sender, pac, () => cantidad, v => cantidad = v);
    }

    [Fact]
    public async Task La_factura_desde_la_version_aprobada_coincide_en_importes_y_muestra_su_proforma()
    {
        var (empresa, db, pedido, sender, pac, _, _) = Escenario();
        using var _db = db;
        var proforma = await GenerarProforma(db, empresa, sender, pedido.Id);
        await Aprobar(db, pedido.Id, proforma.Id);

        var r = await Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, proforma.Id), CancellationToken.None);

        var factura = await db.FacturasVenta.Include(f => f.Lineas).SingleAsync();
        factura.Id.Should().Be(r.Id);
        factura.Estado.Should().Be(EstadoTimbrado.Timbrado);
        factura.Total.Should().Be(proforma.Contenido.Totales.Total);
        factura.ImpuestosTrasladados.Should().Be(proforma.Contenido.Totales.ImpuestosTrasladados);
        ProformaComparador.Comparar(proforma.Contenido, ProformaCfdiMapper.Desde(factura, Ahora, "Cfdi")).Should().BeEmpty();
        pac.UltimaEmision!.Conceptos.Select(c => c.Importe).Should().Equal(proforma.Contenido.Conceptos.Select(c => c.Importe));
        // De cuál proforma viene: el vínculo y el folio PF impreso en el campo «Proforma».
        factura.ProformaPedidoId.Should().Be(proforma.Id);
        factura.Proforma.Should().Be(proforma.Folio);
        (await db.ProformasConformidad.SingleAsync()).Invalidada.Should().BeFalse();
    }

    [Fact]
    public async Task El_pedido_cambiado_despues_de_aprobar_no_emite_invalida_la_conformidad_y_lista_las_diferencias()
    {
        var (empresa, db, pedido, sender, _, _, cambiar) = Escenario();
        using var _db = db;
        var proforma = await GenerarProforma(db, empresa, sender, pedido.Id);
        await Aprobar(db, pedido.Id, proforma.Id);
        cambiar(5m); // el pedido cambia después de la conformidad

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, proforma.Id), CancellationToken.None));

        ex.Code.Should().Be("FAC_PROFORMA_PEDIDO_CAMBIO");
        ex.Message.Should().Contain("Concepto 1 · Cantidad").And.Contain("genera una proforma nueva");
        sender.Emisiones.Should().BeEmpty();
        (await db.FacturasVenta.CountAsync()).Should().Be(0);
        var conformidad = await db.ProformasConformidad.SingleAsync();
        conformidad.Invalidada.Should().BeTrue();
        conformidad.InvalidadaMotivo.Should().Contain("Concepto 1 · Cantidad");
    }

    [Fact]
    public async Task Una_version_con_la_conformidad_invalidada_no_factura_aunque_el_pedido_vuelva_a_ser_igual()
    {
        var (empresa, db, pedido, sender, _, _, cambiar) = Escenario();
        using var _db = db;
        var proforma = await GenerarProforma(db, empresa, sender, pedido.Id);
        await Aprobar(db, pedido.Id, proforma.Id);
        cambiar(5m);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, proforma.Id), CancellationToken.None));
        cambiar(3m);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, proforma.Id), CancellationToken.None));

        ex.Code.Should().Be("FAC_PROFORMA_CONFORMIDAD_INVALIDADA");
        (await db.FacturasVenta.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Una_nueva_version_con_su_conformidad_si_factura_tras_el_cambio()
    {
        var (empresa, db, pedido, sender, _, _, cambiar) = Escenario();
        using var _db = db;
        var v1 = await GenerarProforma(db, empresa, sender, pedido.Id);
        await Aprobar(db, pedido.Id, v1.Id);
        cambiar(5m);
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, v1.Id), CancellationToken.None));

        var v2 = await GenerarProforma(db, empresa, sender, pedido.Id);
        await Aprobar(db, pedido.Id, v2.Id);
        var r = await Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, v2.Id), CancellationToken.None);

        v2.Version.Should().Be(2);
        (await db.FacturasVenta.SingleAsync(f => f.Id == r.Id)).ProformaPedidoId.Should().Be(v2.Id);
        (await db.ProformasConformidad.SingleAsync(c => c.ProformaPedidoId == v1.Id)).Invalidada.Should().BeTrue();
    }

    [Fact]
    public async Task Sin_conformidad_no_se_factura_desde_la_proforma()
    {
        var (empresa, db, pedido, sender, _, _, _) = Escenario();
        using var _db = db;
        var proforma = await GenerarProforma(db, empresa, sender, pedido.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, proforma.Id), CancellationToken.None));

        ex.Code.Should().Be("FAC_PROFORMA_SIN_CONFORMIDAD");
        sender.Emisiones.Should().BeEmpty();
    }

    [Fact]
    public async Task Aprobar_la_version_2_no_habilita_facturar_desde_la_1()
    {
        var (empresa, db, pedido, sender, _, _, _) = Escenario();
        using var _db = db;
        var v1 = await GenerarProforma(db, empresa, sender, pedido.Id);
        var v2 = await GenerarProforma(db, empresa, sender, pedido.Id);
        await Aprobar(db, pedido.Id, v2.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, v1.Id), CancellationToken.None));

        ex.Code.Should().Be("FAC_PROFORMA_SIN_CONFORMIDAD");
    }

    [Fact]
    public async Task Una_emision_fallida_no_invalida_la_conformidad_ni_deja_factura_ligada()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa);
        var sinPac = new SenderEmision(db, empresa, () => Detalle(pedido), new FakeFiscalApiClient());
        var proforma = await GenerarProforma(db, empresa, sinPac, pedido.Id);
        await Aprobar(db, pedido.Id, proforma.Id);
        var sender = new SenderQueFalla(() => Detalle(pedido));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Facturar(db, empresa, sender).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, proforma.Id), CancellationToken.None));

        (await db.ProformasConformidad.SingleAsync()).Invalidada.Should().BeFalse();
        (await db.FacturasVenta.CountAsync()).Should().Be(0);
    }

    private sealed class SenderQueFalla(Func<PedidoFacturableDetalleResponse> detalle) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PedidoFacturableDetalleQuery)
                return Task.FromResult((TResponse)(object)detalle());
            throw new InvalidOperationException("La emisión falló.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Sin_ProformaPedidoId_la_emision_de_siempre_no_cambia()
    {
        var (empresa, db, pedido, sender, _, _, _) = Escenario();
        using var _db = db;
        var comando = Millet.Facturacion.Application.Proformas.ProformaEmisionDesdePedido.Armar(Detalle(pedido), Emisor with { EmpresaId = empresa }).Comando;

        comando.ProformaPedidoId.Should().BeNull();
        var r = await sender.Send(comando, CancellationToken.None);

        var factura = await db.FacturasVenta.SingleAsync(f => f.Id == r.Id);
        factura.ProformaPedidoId.Should().BeNull();
        factura.Proforma.Should().BeNull();
    }

    [Fact]
    public async Task La_exportacion_con_CCE_se_factura_con_el_formulario_no_desde_la_proforma()
    {
        var (empresa, db, pedido, sender, _, _, _) = Escenario();
        using var _db = db;
        var proforma = await GenerarProforma(db, empresa, sender, pedido.Id);
        await Aprobar(db, pedido.Id, proforma.Id);
        var exportacion = new SenderEmision(db, empresa,
            () => Detalle(pedido) with { ComportamientoFiscal = ComportamientoFiscal.ExportacionConCce.ToString() },
            new FakeFiscalApiClient());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Facturar(db, empresa, exportacion).Handle(new EmitirFacturaDesdeProformaCommand(pedido.Id, proforma.Id), CancellationToken.None));

        ex.Code.Should().Be("FAC_PROFORMA_EMISION_NO_DISPONIBLE");
    }
}
