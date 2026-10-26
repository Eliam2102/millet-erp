using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Proformas;
using Millet.Facturacion.Application.Proformas.CompararProformaPedido;
using Millet.Facturacion.Application.Proformas.GenerarProformaPedido;
using Millet.Facturacion.Application.Proformas.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Proformas;

/// <summary>
/// U1.8a: proforma del pedido antes del timbrado.
/// CA-a: proforma y factura timbrada del mismo pedido coinciden en importes,
/// impuestos y receptor (mismo armado <see cref="FacturaVentaArmado"/> y mismo
/// mapeo desde <c>CfdiEmisionBuilder</c>).
/// CA-b: generar la proforma NO reserva folio fiscal ni crea factura.
/// CA-c: un cambio posterior se detecta al comparar contra la última proforma.
/// </summary>
public sealed class ProformaPedidoHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 21, 17, 0, 0, TimeSpan.Zero);
    private static readonly EmpresaFiscalLectura EmisorMillet = new(Guid.Empty, "MIL010101AAA", "Millet", "601", 0.16m, "76120");

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    /// <summary>Pedido A+W con descuento, retenciones y un precio que obliga a redondear centavos por línea.</summary>
    private static PedidoFacturable CrearPedido(FacturacionDbContext db, Guid empresaId, Guid sucursal)
    {
        var p = PedidoFacturable.ImportarDesdeAw(empresaId, "PED-1001", sucursal, Guid.NewGuid(), "Cliente prueba", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        p.AgregarLinea(null, "Vidrio templado 6mm", "30171500", "H87", 3m, 33.335m, 10m, false, tasaIva: 0.16m);
        p.AgregarLinea(null, "Instalación", "72101500", "E48", 1m, 1000m, 0m, false);
        p.RecalcularTotal();
        db.PedidosFacturables.Add(p);
        db.SaveChanges();
        return p;
    }

    /// <summary>Detalle tal como lo devuelve <c>PedidoFacturableDetalleQuery</c> (masters ya resueltos).</summary>
    private static PedidoFacturableDetalleResponse Detalle(PedidoFacturable p, PedidoClienteFiscal? cliente = null,
        decimal cantidadLinea1 = 3m) => new(
        p.Id, p.NumeroPedido, p.Origen.ToString(), p.Estado.ToString(), p.SucursalId, p.ClienteId, p.ClienteNombre,
        p.CanalVentaId, "Mostrador", p.ComportamientoFiscal.ToString(), p.Moneda, p.ObraId, p.ObraNombre, null, null,
        p.Total, null, p.Version,
        cliente ?? new PedidoClienteFiscal("AAA010101AAA", "601", "97000", "G03", "01", "PUE", false),
        [
            new PedidoFacturableLineaDetalle(1, null, "Vidrio templado 6mm", "30171500", "H87", cantidadLinea1, 33.335m, 10m,
                false, "02", 0.16m, 0.106667m, 0.10m),
            // Sin tasa en la línea ni en el master → IVA default de la empresa.
            new PedidoFacturableLineaDetalle(2, null, "Instalación", "72101500", "E48", 1m, 1000m, 0m,
                false, null, null, null, null),
        ]);

    /// <summary>El comando que arma el formulario de emisión (valores.ts + DetallePedido) para el mismo pedido.</summary>
    private static EmitirFacturaVentaCommand ComandoDelFormulario(PedidoFacturable p, decimal cantidadLinea1 = 3m) => new(
        SucursalId: p.SucursalId,
        ReceptorRfc: "AAA010101AAA", ReceptorNombre: "Cliente prueba", ReceptorRegimenFiscal: "601",
        ReceptorCodigoPostal: "97000", ReceptorUsoCfdi: "G03", ReceptorPais: "MEX",
        RfcEmisor: "MIL010101AAA", RegimenFiscalEmisor: "601",
        MetodoPago: "PUE", FormaPago: "01", Moneda: "MXN", TipoCambio: null,
        CanalVenta: 1, ComportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
        ObraId: null, ObraNombre: null, FacturaAgrupada: false,
        Lineas:
        [
            new EmitirFacturaVentaLinea(null, "30171500", "Vidrio templado 6mm", "H87", cantidadLinea1, 33.335m, 10m, "02", 0.16m, 0.106667m, 0.10m),
            new EmitirFacturaVentaLinea(null, "72101500", "Instalación", "E48", 1m, 1000m, 0m, "02", 0.16m, null, null),
        ],
        PedidoFacturableId: p.Id,
        ClienteId: p.ClienteId);

    /// <summary>ISender que responde el detalle del pedido y registra cualquier otra solicitud (p. ej. ReservarFolio).</summary>
    private sealed class SenderDetalle(PedidoFacturableDetalleResponse detalle) : ISender
    {
        public List<object> Otras { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PedidoFacturableDetalleQuery q)
            {
                if (q.Id != detalle.Id)
                    throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", "No existe.");
                return Task.FromResult((TResponse)(object)detalle);
            }
            Otras.Add(request);
            throw new InvalidOperationException($"La proforma no debe enviar {request.GetType().Name}.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotImplementedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private static GenerarProformaPedidoHandler Generar(FacturacionDbContext db, Guid empresaId, ISender sender) =>
        new(db, sender, new FakeEmpresaFiscalReadPort(EmisorMillet with { EmpresaId = empresaId }),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid(), "Cajera Prueba"), new FakeClock(Ahora));

    private static CompararProformaPedidoHandler Comparar(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeAlcanceCajaEvaluator(), new FakeEmpresaFiscalReadPort(EmisorMillet with { EmpresaId = empresaId }),
            new FakeEmpresaContext(empresaId), new FakeClock(Ahora));

    private static EmitirFacturaVentaHandler Emitir(FacturacionDbContext db, Guid empresaId, FakeFiscalApiClient pac) =>
        new(db, new FakeSender(new ReservarFolioResponse("FA-000007", 7, "")), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), pac, new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(EmisorMillet with { EmpresaId = empresaId }),
            new FakeIntegrationEventPublisher(), new FakeContabilidadAsientoPort(),
            new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora),
            ReceptorFiscalTestFactory.Crear(db));

    // ---------- CA-b ----------

    [Fact]
    public async Task CA_b_generar_no_reserva_folio_ni_crea_factura_y_versiona_el_folio_PF()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa, Guid.NewGuid());
        var sender = new SenderDetalle(Detalle(pedido));

        var primera = await Generar(db, empresa, sender).Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None);
        var segunda = await Generar(db, empresa, sender).Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None);

        Assert.Empty(sender.Otras); // nunca ReservarFolioCommand
        Assert.Empty(db.Comprobantes);
        Assert.Equal("PF-PED-1001-1", primera.Folio);
        Assert.Equal(1, primera.Version);
        Assert.Equal("PF-PED-1001-2", segunda.Folio);
        Assert.Equal(2, segunda.Version);
        Assert.Equal("Cajera Prueba", segunda.GeneradaPor);
        Assert.Equal(Ahora, segunda.GeneradaEn);
        Assert.Equal("Cfdi", primera.Contenido.SerieFiscalPrevista);
        Assert.Equal(2, await db.ProformasPedido.CountAsync());
        Assert.Equal(EstadoPedidoFacturable.Importado, pedido.Estado);
    }

    [Fact]
    public async Task Pedido_no_importado_no_genera_proforma()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa, Guid.NewGuid());
        var detalle = Detalle(pedido) with { Estado = EstadoPedidoFacturable.Facturado.ToString() };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Generar(db, empresa, new SenderDetalle(detalle)).Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None));

        Assert.Equal("FAC_PROFORMA_PEDIDO_NO_FACTURABLE", ex.Code);
        Assert.Empty(db.ProformasPedido);
    }

    [Fact]
    public async Task Datos_fiscales_incompletos_no_bloquean_y_se_reportan_como_avisos()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa, Guid.NewGuid());
        var incompleto = new PedidoClienteFiscal(null, null, null, null, null, null, false);

        var r = await Generar(db, empresa, new SenderDetalle(Detalle(pedido, incompleto)))
            .Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None);

        Assert.Contains(r.Avisos, a => a.Contains("RFC"));
        Assert.Contains(r.Avisos, a => a.Contains("régimen fiscal"));
        Assert.Contains(r.Avisos, a => a.Contains("código postal"));
        // Defaults del formulario: uso G03, PUE, forma 01.
        Assert.Equal("G03", r.Contenido.Receptor.UsoCfdi);
        Assert.Equal("PUE", r.Contenido.MetodoPago);
        Assert.Equal("01", r.Contenido.FormaPago);
    }

    // ---------- CA-a ----------

    [Fact]
    public async Task CA_a_proforma_y_factura_timbrada_coinciden_en_importes_impuestos_y_receptor()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa, Guid.NewGuid());
        var proforma = await Generar(db, empresa, new SenderDetalle(Detalle(pedido)))
            .Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None);

        // Antes de timbrar: el mismo payload del formulario no difiere.
        var comparacion = await Comparar(db, empresa)
            .Handle(new CompararProformaPedidoQuery(pedido.Id, ComandoDelFormulario(pedido)), CancellationToken.None);
        Assert.True(comparacion.HayProforma);
        Assert.Equal(proforma.Folio, comparacion.Folio);
        Assert.Empty(comparacion.Diferencias);

        // Se timbra y la factura persistida coincide con la proforma.
        var pac = new FakeFiscalApiClient();
        await Emitir(db, empresa, pac).Handle(ComandoDelFormulario(pedido), CancellationToken.None);
        var factura = await db.FacturasVenta.Include(f => f.Lineas).SingleAsync();
        Assert.Equal(EstadoTimbrado.Timbrado, factura.Estado);

        var timbrada = ProformaCfdiMapper.Desde(factura, Ahora, "Cfdi");
        Assert.Empty(ProformaComparador.Comparar(proforma.Contenido, timbrada));
        Assert.Equal(factura.Total, proforma.Contenido.Totales.Total);
        Assert.Equal(factura.ImpuestosTrasladados, proforma.Contenido.Totales.ImpuestosTrasladados);
        Assert.Equal(factura.Retenciones, proforma.Contenido.Totales.Retenciones);

        // Y con lo que realmente viajó al PAC (conceptos e impuestos).
        var emision = pac.UltimaEmision!;
        Assert.Equal(emision.Receptor.Rfc, proforma.Contenido.Receptor.Rfc);
        Assert.Equal(emision.Conceptos.Select(c => c.Importe), proforma.Contenido.Conceptos.Select(c => c.Importe));
        Assert.Equal(
            emision.Conceptos.Select(c => c.Impuestos.Where(i => !i.EsRetencion).Sum(i => i.Importe)),
            proforma.Contenido.Conceptos.Select(c => c.Iva));
        // Redondeo por línea (centavos): 3 × 33.335 = 100.005 → 100.01; IVA de (100.01 − 10) = 14.40.
        Assert.Equal(100.01m, proforma.Contenido.Conceptos[0].Importe);
        Assert.Equal(14.40m, proforma.Contenido.Conceptos[0].Iva);
    }

    [Fact]
    public void CA_a_armado_compartido_produce_la_misma_proforma_con_folio_de_proforma_o_folio_fiscal()
    {
        var empresa = Guid.NewGuid();
        var cmd = ComandoDelFormulario(PedidoFacturable.ImportarDesdeAw(empresa, "PED-1", Guid.NewGuid(), Guid.NewGuid(),
            "Cliente prueba", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15"));
        var emisor = new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120");
        var receptor = FacturaVentaArmado.ReceptorDe(cmd);

        var comoProforma = FacturaVentaArmado.Construir(cmd, empresa, "PROFORMA", 0, null, receptor, emisor, 2026, 10);
        var comoFactura = FacturaVentaArmado.Construir(cmd, empresa, "FA-000001", 1, Guid.NewGuid(), receptor, emisor, 2026, 10);

        Assert.Empty(ProformaComparador.Comparar(
            ProformaCfdiMapper.Desde(comoProforma, Ahora, "Cfdi"),
            ProformaCfdiMapper.Desde(comoFactura, Ahora, "Cfdi")));
    }

    // ---------- CA-c ----------

    [Fact]
    public async Task CA_c_pedido_modificado_despues_de_la_proforma_muestra_diferencias_al_comparar()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa, Guid.NewGuid());
        await Generar(db, empresa, new SenderDetalle(Detalle(pedido)))
            .Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None);

        var r = await Comparar(db, empresa)
            .Handle(new CompararProformaPedidoQuery(pedido.Id, ComandoDelFormulario(pedido, cantidadLinea1: 5m)), CancellationToken.None);

        Assert.True(r.HayProforma);
        Assert.Equal(1, r.Version);
        Assert.Contains(r.Diferencias, d => d.Campo == "Concepto 1 · Cantidad" && d.Proforma == "3" && d.Timbrado == "5");
        Assert.Contains(r.Diferencias, d => d.Campo == "Total");
    }

    [Fact]
    public async Task Comparar_sin_proforma_no_reporta_diferencias()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa, Guid.NewGuid());

        var r = await Comparar(db, empresa)
            .Handle(new CompararProformaPedidoQuery(pedido.Id, ComandoDelFormulario(pedido)), CancellationToken.None);

        Assert.False(r.HayProforma);
        Assert.Null(r.Folio);
        Assert.Empty(r.Diferencias);
    }

    [Fact]
    public async Task Ultima_proforma_devuelve_la_version_mas_reciente_o_null_sin_proformas()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var pedido = CrearPedido(db, empresa, Guid.NewGuid());
        var ultima = new UltimaProformaPedidoHandler(db, new FakeAlcanceCajaEvaluator());

        Assert.Null(await ultima.Handle(new UltimaProformaPedidoQuery(pedido.Id), CancellationToken.None));

        var sender = new SenderDetalle(Detalle(pedido));
        await Generar(db, empresa, sender).Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None);
        await Generar(db, empresa, sender).Handle(new GenerarProformaPedidoCommand(pedido.Id), CancellationToken.None);

        var r = await ultima.Handle(new UltimaProformaPedidoQuery(pedido.Id), CancellationToken.None);
        Assert.NotNull(r);
        Assert.Equal(2, r.Version);
        Assert.Equal("PF-PED-1001-2", r.Folio);
        Assert.Equal(2, r.Contenido.Conceptos.Count);
    }
}
