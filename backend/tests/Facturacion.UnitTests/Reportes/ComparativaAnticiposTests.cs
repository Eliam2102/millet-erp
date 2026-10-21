using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Anticipos.VincularAnticipo;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Reportes.ComparativaAnticipos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Facturacion.UnitTests.Reportes;

/// <summary>
/// F6.2 — Comparativa de anticipos contra aplicaciones y relación CFDI. Ejercita
/// el ciclo real (emitir anticipo → factura final con amortización → NC 07) y
/// verifica el libro mayor: cobrado, aplicado vigente, saldo a fecha de corte,
/// egresos cancelados, vinculaciones sin egreso y alcance por sucursal.
/// </summary>
public sealed class ComparativaAnticiposTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 18, 0, 0, TimeSpan.Zero);
    private const string RfcCliente = "AAA010101AAA";
    private const short Canal = 7;

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private sealed class Escenario(Guid empresaId) : IDisposable
    {
        public Guid EmpresaId { get; } = empresaId;
        public FacturacionDbContext Db { get; } = NewDb(empresaId);
        public ISender Sender { get; } = new FakeSenderFoliosSecuenciales();
        public FakeFiscalUuidsSecuenciales Fiscal { get; } = new(Ahora);
        public void Dispose() => Db.Dispose();
    }

    private static FakeEmpresaFiscalReadPort EmpresasFiscal(Guid empresaId) =>
        new(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120"));

    private static async Task<EmitirFacturaAnticipoResponse> EmitirAnticipo(
        Escenario e, decimal montoBase, Guid? sucursalId = null, DateTimeOffset? fecha = null)
    {
        e.Fiscal.Fecha = fecha ?? Ahora;
        var handler = new EmitirFacturaAnticipoHandler(
            e.Db, e.Sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), e.Fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(e.EmpresaId), new FakeIntegrationEventPublisher(),
            new FakeEmpresaContext(e.EmpresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(fecha ?? Ahora),
            ReceptorFiscalTestFactory.Crear(e.Db));
        return await handler.Handle(new EmitirFacturaAnticipoCommand(
            sucursalId ?? Guid.NewGuid(), Guid.NewGuid(), RfcCliente, "Cliente Maquila", "601", "97000", "G03", "MEX",
            "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, montoBase, 0.16m,
            null, null, "AW-7", null, null, Canal), CancellationToken.None);
    }

    private static async Task<Guid> EmitirFactura(
        Escenario e, decimal montoBase, IReadOnlyList<AnticipoAAmortizar> anticipos, DateTimeOffset? fecha = null)
    {
        e.Fiscal.Fecha = fecha ?? Ahora;
        var handler = new EmitirFacturaVentaHandler(
            e.Db, e.Sender, new FakePeriodoContablePort(), new FakeCatalogosSatReadPort(), e.Fiscal,
            new FakeCfdiRepositorioPort(), EmpresasFiscal(e.EmpresaId), new FakeIntegrationEventPublisher(),
            new FakeContabilidadAsientoPort(), new FakeEmpresaContext(e.EmpresaId), new FakeUserContext(Guid.NewGuid()),
            new FakeClock(fecha ?? Ahora), ReceptorFiscalTestFactory.Crear(e.Db));
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

    private static Task<Millet.Facturacion.Application.Reportes.ReporteResponse<ComparativaAnticipoFila>> Consultar(
        Escenario e,
        ComparativaAnticiposQuery? query = null,
        AlcanceCajas? alcance = null) =>
        new ComparativaAnticiposHandler(
                e.Db, new FakeAlcanceCajaEvaluator(alcance), new FakeSucursalesReadPort(), new FakeClock(Ahora))
            .Handle(query ?? new ComparativaAnticiposQuery(null, null, null, null, null, null), CancellationToken.None);

    // ---- F6.2-a: anticipo de 30,000 aplicado en 20,000 ----

    [Fact]
    public async Task Anticipo_30000_aplicado_20000_muestra_egreso_y_saldo_10000_parcial()
    {
        using var e = new Escenario(Guid.NewGuid());
        var anticipo = await EmitirAnticipo(e, 25862.07m); // total 30,000.00 con IVA 16 %
        await EmitirFactura(e, 50000m, [new AnticipoAAmortizar(anticipo.AnticipoId, 20000m)]);
        var nc = await e.Db.NotasCredito.SingleAsync();
        var facturaAnticipo = await e.Db.FacturasAnticipo.SingleAsync();

        var reporte = await Consultar(e);

        reporte.Titulo.Should().Be("Comparativa de anticipos vs aplicaciones");
        reporte.Filas.Should().HaveCount(2);
        var filaAnticipo = reporte.Filas[0];
        filaAnticipo.Movimiento.Should().Be(ComparativaAnticiposHandler.MovimientoAnticipo);
        filaAnticipo.Cobrado.Should().Be(30000m);
        filaAnticipo.CobradoSubtotal.Should().Be(facturaAnticipo.Subtotal);
        filaAnticipo.CobradoIva.Should().Be(facturaAnticipo.ImpuestosTrasladados);
        filaAnticipo.Aplicado.Should().Be(20000m);
        filaAnticipo.Saldo.Should().Be(10000m);
        filaAnticipo.Estado.Should().Be("Parcial");
        filaAnticipo.UuidAnticipo.Should().Be(facturaAnticipo.Uuid);
        filaAnticipo.Observacion.Should().BeNull("el saldo calculado coincide con el del sistema");
        filaAnticipo.FacturaAnticipoId.Should().Be(anticipo.FacturaAnticipoId);

        var aplicacion = reporte.Filas[1];
        aplicacion.Movimiento.Should().Be(ComparativaAnticiposHandler.MovimientoAplicacion);
        aplicacion.Aplicado.Should().Be(20000m);
        aplicacion.AplicadoSubtotal.Should().Be(nc.Subtotal);
        aplicacion.AplicadoIva.Should().Be(nc.ImpuestosTrasladados);
        aplicacion.EgresoUuid.Should().Be(nc.Uuid);
        aplicacion.EgresoFolio.Should().Be(nc.Folio);
        aplicacion.FacturaFinalFolio.Should().NotBeNullOrEmpty();
        aplicacion.FacturaFinalUuid.Should().NotBeNullOrEmpty();
        aplicacion.FechaAplicacion.Should().Be(nc.FechaTimbrado);
        aplicacion.EstadoEgreso.Should().Be("Timbrado");
        aplicacion.Cobrado.Should().BeNull();
        aplicacion.Saldo.Should().BeNull();
        aplicacion.Sucursal.Should().Be("MID — Mérida");
        aplicacion.Cliente.Should().Be("Cliente Maquila");
    }

    // ---- CA6.3: dos anticipos aplicados en su totalidad a una factura ----

    [Fact]
    public async Task Dos_anticipos_aplicados_totalmente_quedan_aplicados_con_saldo_cero()
    {
        using var e = new Escenario(Guid.NewGuid());
        var a1 = await EmitirAnticipo(e, 1000m);
        var a2 = await EmitirAnticipo(e, 2000m);
        await EmitirFactura(e, 10000m,
            [new AnticipoAAmortizar(a1.AnticipoId, 1160m), new AnticipoAAmortizar(a2.AnticipoId, 2320m)]);

        var reporte = await Consultar(e);

        var anticipos = reporte.Filas.Where(f => f.Movimiento == ComparativaAnticiposHandler.MovimientoAnticipo).ToList();
        anticipos.Should().HaveCount(2);
        anticipos.Should().AllSatisfy(f =>
        {
            f.Saldo.Should().Be(0m);
            f.Estado.Should().Be("Aplicado");
        });
        reporte.Filas.Count(f => f.Movimiento == ComparativaAnticiposHandler.MovimientoAplicacion).Should().Be(2);
        // Las dos aplicaciones apuntan a la misma factura final.
        reporte.Filas.Where(f => f.Movimiento == ComparativaAnticiposHandler.MovimientoAplicacion)
            .Select(f => f.FacturaFinalUuid).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Egreso_cancelado_se_muestra_pero_no_resta_saldo()
    {
        using var e = new Escenario(Guid.NewGuid());
        var anticipo = await EmitirAnticipo(e, 1000m);
        await EmitirFactura(e, 10000m, [new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]);
        var nc = await e.Db.NotasCredito.SingleAsync();
        nc.MarcarCancelacionPendiente();
        nc.MarcarCancelado();
        await e.Db.SaveChangesAsync();

        var reporte = await Consultar(e);

        var filaAnticipo = reporte.Filas[0];
        filaAnticipo.Aplicado.Should().Be(0m);
        filaAnticipo.Saldo.Should().Be(1160m);
        filaAnticipo.Estado.Should().Be("Pendiente");
        // El agregado aún tiene 660 (restitución fuera de alcance): se avisa.
        filaAnticipo.Observacion.Should().StartWith("Saldo en sistema");

        var aplicacion = reporte.Filas[1];
        aplicacion.EstadoEgreso.Should().Be("Cancelado");
        aplicacion.Aplicado.Should().BeNull();
        aplicacion.Observacion.Should().Be("Egreso cancelado: no resta saldo");
    }

    [Fact]
    public async Task Egreso_en_cancelacion_pendiente_sigue_restando_saldo()
    {
        using var e = new Escenario(Guid.NewGuid());
        var anticipo = await EmitirAnticipo(e, 1000m);
        await EmitirFactura(e, 10000m, [new AnticipoAAmortizar(anticipo.AnticipoId, 500m)]);
        var nc = await e.Db.NotasCredito.SingleAsync();
        nc.MarcarCancelacionPendiente();
        await e.Db.SaveChangesAsync();

        var reporte = await Consultar(e);

        reporte.Filas[0].Saldo.Should().Be(660m);
        reporte.Filas[1].EstadoEgreso.Should().Be("Cancelación pendiente");
        reporte.Filas[1].Aplicado.Should().Be(500m);
    }

    [Fact]
    public async Task Anticipo_de_sucursal_fuera_de_alcance_no_se_ve()
    {
        using var e = new Escenario(Guid.NewGuid());
        var sucursalPropia = Guid.NewGuid();
        await EmitirAnticipo(e, 1000m, sucursalPropia);
        await EmitirAnticipo(e, 2000m, Guid.NewGuid());

        var reporte = await Consultar(e,
            alcance: AlcanceCajas.De([new CombinacionAlcance(sucursalPropia, null)]));

        var fila = reporte.Filas.Should().ContainSingle().Subject;
        fila.Cobrado.Should().Be(1160m);

        var sinAlcance = await Consultar(e, alcance: AlcanceCajas.Ninguno());
        sinAlcance.Filas.Should().BeEmpty();
    }

    [Fact]
    public async Task Filtro_por_sucursal_y_por_estado()
    {
        using var e = new Escenario(Guid.NewGuid());
        var sucursal = Guid.NewGuid();
        var a1 = await EmitirAnticipo(e, 1000m, sucursal);
        await EmitirAnticipo(e, 2000m, sucursal);
        await EmitirAnticipo(e, 3000m);
        await EmitirFactura(e, 10000m, [new AnticipoAAmortizar(a1.AnticipoId, 1160m)]);

        var porSucursal = await Consultar(e, new ComparativaAnticiposQuery(sucursal, null, null, null, null, null));
        porSucursal.Filas.Count(f => f.Movimiento == ComparativaAnticiposHandler.MovimientoAnticipo).Should().Be(2);

        var aplicados = await Consultar(e, new ComparativaAnticiposQuery(
            null, null, null, null, null, EstadoComparativaAnticipo.Aplicado));
        aplicados.Filas.Should().HaveCount(2, "el anticipo aplicado arrastra su aplicación");
        aplicados.Filas.Should().AllSatisfy(f => f.AnticipoId.Should().Be(a1.AnticipoId));

        var pendientes = await Consultar(e, new ComparativaAnticiposQuery(
            null, null, null, null, null, EstadoComparativaAnticipo.Pendiente));
        pendientes.Filas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Totales_cuadran_cobrado_menos_aplicado_igual_saldo()
    {
        using var e = new Escenario(Guid.NewGuid());
        var a1 = await EmitirAnticipo(e, 1000m);
        var a2 = await EmitirAnticipo(e, 2500m);
        await EmitirAnticipo(e, 400m);
        await EmitirFactura(e, 10000m,
            [new AnticipoAAmortizar(a1.AnticipoId, 700m), new AnticipoAAmortizar(a2.AnticipoId, 2900m)]);

        var reporte = await Consultar(e);

        var t = reporte.Totales!;
        t["cobrado"].Should().Be(1160m + 2900m + 464m);
        t["aplicado"].Should().Be(700m + 2900m);
        (t["cobrado"] - t["aplicado"]).Should().Be(t["saldo"]);
        t["cobradoSubtotal"].Should().Be(3900m);
        (t["aplicadoSubtotal"] + t["aplicadoIva"]).Should().Be(t["aplicado"]);
    }

    [Fact]
    public async Task Fecha_de_corte_excluye_aplicaciones_posteriores()
    {
        using var e = new Escenario(Guid.NewGuid());
        var anticipo = await EmitirAnticipo(e, 1000m, fecha: Ahora.AddDays(-10));
        await EmitirFactura(e, 10000m, [new AnticipoAAmortizar(anticipo.AnticipoId, 300m)], Ahora.AddDays(-5));
        await EmitirFactura(e, 10000m, [new AnticipoAAmortizar(anticipo.AnticipoId, 400m)], Ahora);

        var alCorte = await Consultar(e, new ComparativaAnticiposQuery(
            null, null, null, null, Ahora.AddDays(-3), null));

        alCorte.Filas.Should().HaveCount(2);
        alCorte.Filas[0].Aplicado.Should().Be(300m);
        alCorte.Filas[0].Saldo.Should().Be(860m);
        alCorte.Filas[0].Observacion.Should().BeNull("con corte pasado no se compara contra el saldo actual");

        var hoy = await Consultar(e);
        hoy.Filas.Should().HaveCount(3);
        hoy.Filas[0].Saldo.Should().Be(460m);
        hoy.Filas[1].FechaAplicacion.Should().BeBefore(hoy.Filas[2].FechaAplicacion!.Value);

        // Un anticipo timbrado después del corte no aparece.
        var antes = await Consultar(e, new ComparativaAnticiposQuery(
            null, null, null, null, Ahora.AddDays(-20), null));
        antes.Filas.Should().BeEmpty();
    }

    [Fact]
    public async Task Periodo_filtra_por_fecha_de_timbrado_del_anticipo()
    {
        using var e = new Escenario(Guid.NewGuid());
        await EmitirAnticipo(e, 1000m, fecha: Ahora.AddDays(-40));
        await EmitirAnticipo(e, 2000m, fecha: Ahora.AddDays(-2));

        var reporte = await Consultar(e, new ComparativaAnticiposQuery(
            null, null, Ahora.AddDays(-7), Ahora, null, null));

        reporte.Filas.Should().ContainSingle().Which.Cobrado.Should().Be(2320m);
    }

    [Fact]
    public async Task Vinculacion_sin_egreso_va_a_comprometido_y_no_resta_saldo()
    {
        using var e = new Escenario(Guid.NewGuid());
        var anticipo = await EmitirAnticipo(e, 1000m);
        var facturaId = await EmitirFactura(e, 10000m, []);
        await new VincularAnticipoHandler(e.Db, new FakeClock(Ahora))
            .Handle(new VincularAnticipoCommand(anticipo.AnticipoId, facturaId, 200m), CancellationToken.None);

        var reporte = await Consultar(e);

        reporte.Filas.Should().HaveCount(2);
        reporte.Filas[0].Comprometido.Should().Be(200m);
        reporte.Filas[0].Saldo.Should().Be(1160m);
        reporte.Filas[0].Estado.Should().Be("Pendiente");
        var vinculacion = reporte.Filas[1];
        vinculacion.Movimiento.Should().Be(ComparativaAnticiposHandler.MovimientoVinculacionSinEgreso);
        vinculacion.Comprometido.Should().Be(200m);
        vinculacion.Aplicado.Should().BeNull();
        vinculacion.EgresoUuid.Should().BeNull();
        vinculacion.FacturaFinalFolio.Should().NotBeNullOrEmpty();
        reporte.Totales!["comprometido"].Should().Be(200m);
    }

    // ---- Validador ----

    [Fact]
    public void Validador_rechaza_ids_vacios_rango_invertido_corte_previo_y_estado_invalido()
    {
        var v = new ComparativaAnticiposValidator();
        var desde = Ahora.AddDays(-10);

        v.Validate(new ComparativaAnticiposQuery(null, null, desde, Ahora, Ahora, EstadoComparativaAnticipo.Parcial))
            .IsValid.Should().BeTrue();
        v.Validate(new ComparativaAnticiposQuery(Guid.Empty, null, null, null, null, null)).IsValid.Should().BeFalse();
        v.Validate(new ComparativaAnticiposQuery(null, Guid.Empty, null, null, null, null)).IsValid.Should().BeFalse();
        v.Validate(new ComparativaAnticiposQuery(null, null, Ahora, desde, null, null)).IsValid.Should().BeFalse();
        v.Validate(new ComparativaAnticiposQuery(null, null, Ahora, null, desde, null)).IsValid.Should().BeFalse();
        v.Validate(new ComparativaAnticiposQuery(null, null, null, null, null, (EstadoComparativaAnticipo)99))
            .IsValid.Should().BeFalse();
    }
}
