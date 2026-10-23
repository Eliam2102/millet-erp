using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Anticipos.VincularAnticipo;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Reportes.ComparativaAnticipos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Facturacion.UnitTests.Reportes;

/// <summary>
/// F1-CXC-05 — adapter público que expone a CxC los anticipos con sus
/// aplicaciones a factura final. Ejercita el ciclo real (anticipo → factura
/// final con amortización → NC 07) igual que <see cref="ComparativaAnticiposTests"/>
/// y verifica paridad de cálculo contra la comparativa F6.2.
/// </summary>
public sealed class AplicacionesAnticipoReadAdapterTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);
    private const string RfcCliente = "AAA010101AAA";
    private const short Canal = 7;

    private sealed class Escenario : IDisposable
    {
        public Guid EmpresaId { get; } = Guid.NewGuid();
        public FacturacionDbContext Db { get; }
        public ISender Sender { get; } = new FakeSenderFoliosSecuenciales();
        public FakeFiscalUuidsSecuenciales Fiscal { get; } = new(Ahora);

        public Escenario()
        {
            Db = new FacturacionDbContext(new DbContextOptionsBuilder<FacturacionDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options,
                new FakeEmpresaContext(EmpresaId));
        }

        public void Dispose() => Db.Dispose();
    }

    private static FakeEmpresaFiscalReadPort EmpresasFiscal(Guid empresaId) =>
        new(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));

    private static async Task<EmitirFacturaAnticipoResponse> EmitirAnticipo(
        Escenario e, decimal montoBase, Guid? clienteId = null, string nombre = "Cliente Maquila")
    {
        e.Fiscal.Fecha = Ahora;
        var handler = new EmitirFacturaAnticipoHandler(
            e.Db, e.Sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), e.Fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(e.EmpresaId), new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(e.EmpresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora),
            ReceptorFiscalTestFactory.Crear(e.Db));
        return await handler.Handle(new EmitirFacturaAnticipoCommand(
            Guid.NewGuid(), clienteId ?? Guid.NewGuid(), RfcCliente, nombre, "601", "97000", "G03", "MEX",
            "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, montoBase, 0.16m,
            null, null, "AW-7", null, null, Canal), CancellationToken.None);
    }

    private static async Task<Guid> EmitirFactura(
        Escenario e, decimal montoBase, IReadOnlyList<AnticipoAAmortizar> anticipos)
    {
        e.Fiscal.Fecha = Ahora;
        var handler = new EmitirFacturaVentaHandler(
            e.Db, e.Sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), e.Fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(e.EmpresaId), new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(), new FakeEmpresaContext(e.EmpresaId), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(Ahora), ReceptorFiscalTestFactory.Crear(e.Db));
        var resp = await handler.Handle(new EmitirFacturaVentaCommand(
            SucursalId: Guid.NewGuid(),
            ReceptorRfc: RfcCliente, ReceptorNombre: "Cliente Maquila", ReceptorRegimenFiscal: "601",
            ReceptorCodigoPostal: "97000", ReceptorUsoCfdi: "G03", ReceptorPais: "MEX",
            RfcEmisor: "BBB010101BBB", RegimenFiscalEmisor: "601",
            MetodoPago: "PUE", FormaPago: "03", Moneda: "MXN", TipoCambio: null,
            CanalVenta: Canal,
            ComportamientoFiscal: ComportamientoFiscal.ConAnticipo,
            ObraId: null, ObraNombre: null, FacturaAgrupada: false,
            Lineas: [new EmitirFacturaVentaLinea(null, "01010101", "Servicio maquila", "E48", 1m, montoBase, 0m, "02", 0.16m, null, null)],
            Anticipos: anticipos), CancellationToken.None);
        resp.Estado.Should().Be("Timbrado");
        return resp.Id;
    }

    private static Task<IReadOnlyList<AnticipoConAplicacionesDto>> Listar(Escenario e, Guid? clienteId = null) =>
        new FacturacionAplicacionesAnticipoReadAdapter(e.Db, new FakeSucursalesReadPort())
            .ListarAsync(clienteId, CancellationToken.None);

    // ---- (a) 30,000 aplicado en 20,000 ----

    [Fact]
    public async Task Anticipo_30000_aplicado_20000_queda_parcial_con_saldo_10000_y_su_aplicacion()
    {
        using var e = new Escenario();
        var clienteId = Guid.NewGuid();
        var anticipo = await EmitirAnticipo(e, 25862.07m, clienteId); // total 30,000.00 con IVA 16 %
        var facturaId = await EmitirFactura(e, 50000m, [new AnticipoAAmortizar(anticipo.AnticipoId, 20000m)]);
        var nc = await e.Db.NotasCredito.SingleAsync();
        var facturaAnticipo = await e.Db.FacturasAnticipo.SingleAsync();
        var facturaFinal = await e.Db.FacturasVenta.SingleAsync(f => f.Id == facturaId);

        var lista = await Listar(e);

        var a = lista.Should().ContainSingle().Subject;
        a.AnticipoId.Should().Be(anticipo.AnticipoId);
        a.FacturaAnticipoId.Should().Be(anticipo.FacturaAnticipoId);
        a.ClienteId.Should().Be(clienteId);
        a.ClienteNombre.Should().Be("Cliente Maquila");
        a.Folio.Should().Be(facturaAnticipo.Folio);
        a.Uuid.Should().Be(facturaAnticipo.Uuid);
        a.FechaTimbrado.Should().Be(facturaAnticipo.FechaTimbrado);
        a.SucursalId.Should().Be(facturaAnticipo.SucursalId);
        a.SucursalNombre.Should().Be("MID — Mérida");
        a.Moneda.Should().Be("MXN");
        a.PedidoOrigenRef.Should().Be((await e.Db.Anticipos.SingleAsync()).PedidoOrigenRef);
        a.Cobrado.Should().Be(30000m);
        a.Aplicado.Should().Be(20000m);
        a.Comprometido.Should().Be(0m);
        a.Saldo.Should().Be(10000m);
        a.SaldoSistema.Should().Be(10000m);
        a.Estado.Should().Be(EstadoAnticipoAplicaciones.Parcial);
        a.Observacion.Should().BeNull();

        var ap = a.Aplicaciones.Should().ContainSingle().Subject;
        ap.Tipo.Should().Be(TipoAplicacionAnticipo.Aplicacion);
        ap.FacturaFinalFolio.Should().Be(facturaFinal.Folio);
        ap.FacturaFinalUuid.Should().Be(facturaFinal.Uuid).And.NotBeNullOrEmpty();
        ap.EgresoFolio.Should().Be(nc.Folio);
        ap.EgresoUuid.Should().Be(nc.Uuid).And.NotBeNullOrEmpty();
        ap.FechaAplicacion.Should().Be(nc.FechaTimbrado);
        ap.EstadoEgreso.Should().Be("Timbrado");
        ap.Importe.Should().Be(20000m);
    }

    // ---- (b) una factura final amortiza dos anticipos ----

    [Fact]
    public async Task Factura_que_amortiza_dos_anticipos_aparece_en_ambos_con_saldo_cero()
    {
        using var e = new Escenario();
        var a1 = await EmitirAnticipo(e, 1000m);
        var a2 = await EmitirAnticipo(e, 2000m);
        var facturaId = await EmitirFactura(e, 10000m,
            [new AnticipoAAmortizar(a1.AnticipoId, 1160m), new AnticipoAAmortizar(a2.AnticipoId, 2320m)]);
        var facturaFinal = await e.Db.FacturasVenta.SingleAsync(f => f.Id == facturaId);

        var lista = await Listar(e);

        lista.Should().HaveCount(2);
        lista.Should().AllSatisfy(a =>
        {
            a.Saldo.Should().Be(0m);
            a.Aplicado.Should().Be(a.Cobrado);
            a.Estado.Should().Be(EstadoAnticipoAplicaciones.Aplicado);
            a.Aplicaciones.Should().ContainSingle()
                .Which.FacturaFinalUuid.Should().Be(facturaFinal.Uuid);
        });
    }

    // ---- (c) vinculación sin egreso y egreso cancelado ----

    [Fact]
    public async Task Vinculacion_sin_egreso_va_a_comprometido_y_no_resta()
    {
        using var e = new Escenario();
        var anticipo = await EmitirAnticipo(e, 1000m);
        var facturaId = await EmitirFactura(e, 10000m, []);
        await new VincularAnticipoHandler(e.Db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(anticipo.AnticipoId, facturaId, 200m), CancellationToken.None);

        var a = (await Listar(e)).Should().ContainSingle().Subject;

        a.Comprometido.Should().Be(200m);
        a.Aplicado.Should().Be(0m);
        a.Saldo.Should().Be(1160m);
        a.Estado.Should().Be(EstadoAnticipoAplicaciones.Pendiente);
        var ap = a.Aplicaciones.Should().ContainSingle().Subject;
        ap.Tipo.Should().Be(TipoAplicacionAnticipo.VinculacionSinEgreso);
        ap.Importe.Should().Be(200m);
        ap.EgresoUuid.Should().BeNull();
        ap.FacturaFinalFolio.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Egreso_cancelado_se_muestra_no_resta_y_avisa_saldo_en_sistema()
    {
        using var e = new Escenario();
        var anticipo = await EmitirAnticipo(e, 1000m);
        await EmitirFactura(e, 10000m, [new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]);
        var nc = await e.Db.NotasCredito.SingleAsync();
        nc.MarcarCancelacionPendiente();
        nc.MarcarCancelado();
        await e.Db.SaveChangesAsync();

        var a = (await Listar(e)).Should().ContainSingle().Subject;

        a.Aplicado.Should().Be(0m);
        a.Saldo.Should().Be(1160m);
        a.SaldoSistema.Should().Be(660m, "la restitución del saldo al cancelar el egreso está fuera de alcance");
        a.Estado.Should().Be(EstadoAnticipoAplicaciones.Pendiente);
        a.Observacion.Should().StartWith("Saldo en sistema");
        var ap = a.Aplicaciones.Should().ContainSingle().Subject;
        ap.Tipo.Should().Be(TipoAplicacionAnticipo.EgresoCancelado);
        ap.EstadoEgreso.Should().Be("Cancelado");
        ap.EgresoUuid.Should().Be(nc.Uuid);
        ap.Importe.Should().Be(500m);
    }

    [Fact]
    public async Task Anticipo_cancelado_se_incluye_con_estado_cancelado()
    {
        using var e = new Escenario();
        var anticipo = await EmitirAnticipo(e, 1000m);
        var agregado = await e.Db.Anticipos.SingleAsync(a => a.Id == anticipo.AnticipoId);
        agregado.Cancelar();
        await e.Db.SaveChangesAsync();

        var a = (await Listar(e)).Should().ContainSingle().Subject;

        a.Estado.Should().Be(EstadoAnticipoAplicaciones.Cancelado);
    }

    // ---- (d) paridad con la comparativa F6.2 ----

    [Fact]
    public async Task Paridad_con_la_comparativa_de_facturacion()
    {
        using var e = new Escenario();
        var a1 = await EmitirAnticipo(e, 1000m);
        var a2 = await EmitirAnticipo(e, 2500m);
        var a3 = await EmitirAnticipo(e, 400m);
        var a4 = await EmitirAnticipo(e, 800m);
        var a5 = await EmitirAnticipo(e, 300m);
        await EmitirFactura(e, 10000m,
            [new AnticipoAAmortizar(a1.AnticipoId, 700m), new AnticipoAAmortizar(a2.AnticipoId, 2900m)]);
        await EmitirFactura(e, 10000m, [new AnticipoAAmortizar(a4.AnticipoId, 300m)]);
        var ncCancelada = await e.Db.NotasCredito.SingleAsync(n => n.AnticipoOrigenId == a4.AnticipoId);
        ncCancelada.MarcarCancelacionPendiente();
        ncCancelada.MarcarCancelado();
        var sinEgreso = await EmitirFactura(e, 10000m, []);
        await e.Db.SaveChangesAsync();
        await new VincularAnticipoHandler(e.Db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(a3.AnticipoId, sinEgreso, 100m), CancellationToken.None);
        (await e.Db.Anticipos.SingleAsync(a => a.Id == a5.AnticipoId)).Cancelar();
        await e.Db.SaveChangesAsync();

        var comparativa = await new ComparativaAnticiposHandler(
                e.Db, new FakeAlcanceCajaEvaluator(), new FakeSucursalesReadPort(), new FakeClock(Ahora))
            .Handle(new ComparativaAnticiposQuery(null, null, null, null, null, null), CancellationToken.None);
        var lista = await Listar(e);

        var filas = comparativa.Filas
            .Where(f => f.Movimiento == ComparativaAnticiposHandler.MovimientoAnticipo)
            .ToDictionary(f => f.AnticipoId);
        lista.Should().HaveCount(filas.Count).And.HaveCount(5);
        foreach (var a in lista)
        {
            var f = filas[a.AnticipoId];
            a.Cobrado.Should().Be(f.Cobrado!.Value);
            a.Aplicado.Should().Be(f.Aplicado!.Value);
            a.Comprometido.Should().Be(f.Comprometido!.Value);
            a.Saldo.Should().Be(f.Saldo!.Value);
            a.Estado.Should().Be(f.Estado);
            a.Observacion.Should().Be(f.Observacion);
            a.Folio.Should().Be(f.FolioAnticipo);
            a.Aplicaciones.Should().HaveCount(
                comparativa.Filas.Count(x => x.AnticipoId == a.AnticipoId
                                             && x.Movimiento != ComparativaAnticiposHandler.MovimientoAnticipo));
        }
    }

    // ---- (e) filtro por cliente ----

    [Fact]
    public async Task Sin_cliente_lista_todos_y_con_cliente_solo_los_suyos()
    {
        using var e = new Escenario();
        var clienteA = Guid.NewGuid();
        var clienteB = Guid.NewGuid();
        await EmitirAnticipo(e, 1000m, clienteA, "Cliente A");
        await EmitirAnticipo(e, 2000m, clienteA, "Cliente A");
        await EmitirAnticipo(e, 3000m, clienteB, "Cliente B");

        var todos = await Listar(e);
        todos.Should().HaveCount(3);
        todos.Select(a => a.ClienteId).Distinct().Should().BeEquivalentTo([clienteA, clienteB]);

        var soloA = await Listar(e, clienteA);
        soloA.Should().HaveCount(2).And.OnlyContain(a => a.ClienteId == clienteA && a.ClienteNombre == "Cliente A");

        (await Listar(e, Guid.NewGuid())).Should().BeEmpty();
    }
}
