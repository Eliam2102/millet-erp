using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// Cobro de mostrador (CAJAS-PR4, 12-cajas.md §6): registrar a la sesión
/// abierta del cajero, un movimiento por forma [12-5], AsignarCajaCobro como
/// única vía de escritura de CajaId, y cancelación con reversa o ajuste
/// pendiente [12-C].
/// </summary>
public sealed class CobrosMostradorTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 7, 10, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    private static DatosFiscalesReceptor Receptor() =>
        new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    private static DatosFiscalesEmisor Emisor() => new("BBB010101BBB", "Millet", "601", "76120");

    private static FacturaVenta FacturaTimbrada(decimal valor, string metodoPago = "PUE", string folio = "F-1")
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, folio, 1, SucursalId, null, null, Receptor(), Emisor(),
            metodoPago, "01", "MXN", null, 2026, 7, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return fv;
    }

    /// <summary>NC que acredita a la factura ([Decisión 13-K]); timbrada salvo que se indique.</summary>
    private static NotaCredito NcAplicada(Guid facturaId, decimal monto, string folio = "NC-1", bool timbrada = true)
    {
        var nc = NotaCredito.CrearBonificacion(Empresa, folio, 1, SucursalId, null, null, Receptor(), Emisor(),
            "01", "MXN", null, 2026, 7, 1, facturaId, monto, null, null);
        if (timbrada)
        {
            nc.MarcarTimbradoEnProceso();
            nc.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        }
        return nc;
    }

    private static async Task<(Caja Caja, CajaSesion Sesion)> SembrarSesionAsync(
        FacturacionDbContext db, Guid? responsable = null)
    {
        var caja = Caja.Crear(Empresa, "Caja Mostrador", null);
        var sesion = CajaSesion.Abrir(
            Empresa, caja.Id, SucursalId, responsable ?? Cajero, 500m,
            new DateOnly(2026, 7, 10), Ahora.AddHours(-2), null);
        db.Cajas.Add(caja);
        db.CajaSesiones.Add(sesion);
        await db.SaveChangesAsync();
        return (caja, sesion);
    }

    private static RegistrarCobroMostradorHandler RegistrarHandler(
        FacturacionDbContext db,
        FakeIntegrationEventPublisher? eventos = null,
        IAlcanceCajaEvaluator? alcance = null,
        Guid? usuario = null) =>
        new(db, new FakeEmpresaContext(Empresa), new FakeUserContext(usuario ?? Cajero), new FakeClock(Ahora),
            new FakeSucursalesReadPort(), alcance ?? new FakeAlcanceCajaEvaluator(), eventos ?? new FakeIntegrationEventPublisher());

    private static CancelarCobroMostradorHandler CancelarHandler(
        FacturacionDbContext db,
        FakeIntegrationEventPublisher? eventos = null,
        Guid? usuario = null) =>
        new(db, new FakeEmpresaContext(Empresa), new FakeUserContext(usuario ?? Cajero), new FakeClock(Ahora),
            new FakeSucursalesReadPort(), eventos ?? new FakeIntegrationEventPublisher());

    // ---- Registrar ----

    [Fact]
    public async Task Registrar_crea_cobro_movimientos_por_forma_y_asigna_caja_al_comprobante()
    {
        using var db = NewDb();
        var (caja, sesion) = await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(1000m);
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();

        var eventos = new FakeIntegrationEventPublisher();
        var response = await RegistrarHandler(db, eventos).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 600m), new CobroFormaPagoInput("04", 400m, "AUTH-77")]),
            CancellationToken.None);

        response.Total.Should().Be(1000m);
        response.CajaSesionId.Should().Be(sesion.Id);

        var cobro = await db.CobrosMostrador.Include(c => c.FormasPago).SingleAsync();
        cobro.Estado.Should().Be(EstadoCobroMostrador.Registrado);
        cobro.SucursalId.Should().Be(SucursalId);
        cobro.CanalVentaId.Should().Be((short)1);
        cobro.FormasPago.Should().HaveCount(2);

        var movimientos = await db.CajaMovimientos
            .Where(m => m.Tipo == TipoCajaMovimiento.CobroCliente).ToListAsync();
        movimientos.Should().HaveCount(2);
        movimientos.Sum(m => m.Importe).Should().Be(1000m);
        movimientos.Should().OnlyContain(m => m.CobroMostradorId == cobro.Id);

        factura.CajaId.Should().Be(caja.Id); // AsignarCajaCobro (§6)
        eventos.Publicados.Should().ContainSingle()
            .Which.Should().BeOfType<CobroMostradorRegistradoIntegrationEvent>();
    }

    [Fact]
    public async Task Registrar_sin_sesion_abierta_falla()
    {
        using var db = NewDb();
        var factura = FacturaTimbrada(100m);
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();

        var act = () => RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 100m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COBRO_SIN_SESION");
    }

    [Fact]
    public async Task Registrar_valida_total_ppd_doble_cobro_y_alcance()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(1000m);
        var ppd = FacturaTimbrada(500m, metodoPago: "PPD", folio: "F-2");
        db.FacturasVenta.AddRange(factura, ppd);
        await db.SaveChangesAsync();

        // Suma ≠ total.
        var mismatch = () => RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 999m)]), CancellationToken.None);
        await mismatch.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COBRO_TOTAL_NO_COINCIDE");

        // PPD directa → vía REPP.
        var esPpd = () => RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            ppd.Id, [new CobroFormaPagoInput("01", 500m)]), CancellationToken.None);
        await esPpd.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COBRO_FACTURA_PPD");

        // Doble cobro → 409.
        await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 1000m)]), CancellationToken.None);
        var doble = () => RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 1000m)]), CancellationToken.None);
        await doble.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "COBRO_YA_REGISTRADO");

        // Fuera del alcance del cajero → mismo 404 que inexistente (§4.1).
        var fuera = () => RegistrarHandler(db, alcance: new FakeAlcanceCajaEvaluator(AlcanceCajas.Ninguno()))
            .Handle(new RegistrarCobroMostradorCommand(
                factura.Id, [new CobroFormaPagoInput("01", 1000m)]), CancellationToken.None);
        await fuera.Should().ThrowAsync<EntityNotFoundException>();
    }

    // ---- [Decisión 13-K]: el cobro espera el total neto de NC ----

    [Fact]
    public async Task Registrar_espera_el_total_neto_de_notas_de_credito()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(1000m);
        db.FacturasVenta.Add(factura);
        db.NotasCredito.Add(NcAplicada(factura.Id, 300m));
        await db.SaveChangesAsync();

        // El total bruto del CFDI ya no coincide…
        var bruto = () => RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 1000m)]), CancellationToken.None);
        await bruto.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COBRO_TOTAL_NO_COINCIDE");

        // …se cobra el neto (1000 − 300).
        var response = await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 700m)]), CancellationToken.None);
        response.Total.Should().Be(700m);
    }

    [Fact]
    public async Task Registrar_factura_totalmente_acreditada_lanza_sin_saldo()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(400m);
        db.FacturasVenta.Add(factura);
        db.NotasCredito.Add(NcAplicada(factura.Id, 400m));
        await db.SaveChangesAsync();

        var act = () => RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 400m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COBRO_SIN_SALDO");
    }

    [Fact]
    public async Task Registrar_nc_no_timbrada_no_acredita()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(500m);
        db.FacturasVenta.Add(factura);
        db.NotasCredito.Add(NcAplicada(factura.Id, 100m, timbrada: false));
        await db.SaveChangesAsync();

        var response = await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 500m)]), CancellationToken.None);

        response.Total.Should().Be(500m);
    }

    [Fact]
    public async Task Cobrables_muestra_neto_y_oculta_totalmente_acreditadas()
    {
        using var db = NewDb();
        var conNc = FacturaTimbrada(1000m, folio: "F-NC");
        var saldada = FacturaTimbrada(400m, folio: "F-SAL");
        db.FacturasVenta.AddRange(conNc, saldada);
        db.NotasCredito.Add(NcAplicada(conNc.Id, 300m));
        db.NotasCredito.Add(NcAplicada(saldada.Id, 400m, folio: "NC-2"));
        await db.SaveChangesAsync();

        var items = await new ListarComprobantesCobrablesHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ListarComprobantesCobrablesQuery(), CancellationToken.None);

        var item = items.Should().ContainSingle(i => i.ComprobanteId == conNc.Id).Subject;
        item.Total.Should().Be(700m);
        item.MontoAcreditado.Should().Be(300m);
        items.Should().NotContain(i => i.ComprobanteId == saldada.Id);
    }

    [Fact]
    public async Task Cobrables_desglosa_la_ranura_del_resto_de_NC()
    {
        // RANURA-PR3: una factura con NC de ranura (motivo Ranura) + NC de
        // bonificación — el item expone el total acreditado Y la porción
        // que es ranura, para que el cajero la vea desglosada.
        using var db = NewDb();
        var factura = FacturaTimbrada(1000m, folio: "F-RAN");
        db.FacturasVenta.Add(factura);
        var ranura = NotaCredito.CrearRanura(Empresa, "NC-R", 1, SucursalId, null, null, Receptor(), Emisor(),
            "01", "MXN", null, 2026, 7, 1, factura.Id, 58m, 0.16m, "Ranura pedido AW-1");
        ranura.MarcarTimbradoEnProceso();
        ranura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.NotasCredito.Add(ranura);
        db.NotasCredito.Add(NcAplicada(factura.Id, 100m, folio: "NC-B"));
        await db.SaveChangesAsync();

        var items = await new ListarComprobantesCobrablesHandler(db, new FakeAlcanceCajaEvaluator())
            .Handle(new ListarComprobantesCobrablesQuery(), CancellationToken.None);

        var item = items.Should().ContainSingle(i => i.ComprobanteId == factura.Id).Subject;
        item.Total.Should().Be(842m);           // 1000 − 58 − 100
        item.MontoAcreditado.Should().Be(158m); // todas las NC timbradas
        item.MontoRanura.Should().Be(58m);      // solo la de motivo Ranura
    }

    [Fact]
    public async Task Registrar_repp_cobra_el_importe_del_complemento()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var repp = ReciboPago.CrearBorrador(Empresa, "P-1", 1, SucursalId, null, null, Receptor(), Emisor(),
            2026, 7, Ahora, "MXN", 1);
        repp.EstablecerImporteTotalPago(750m);
        repp.MarcarTimbradoEnProceso();
        repp.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.RecibosPago.Add(repp);
        await db.SaveChangesAsync();

        var response = await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            repp.Id, [new CobroFormaPagoInput("01", 750m)], OrigenCobroMostrador.LiquidacionRuta),
            CancellationToken.None);

        response.Total.Should().Be(750m);
        (await db.CobrosMostrador.SingleAsync()).Origen.Should().Be(OrigenCobroMostrador.LiquidacionRuta);
    }

    // ---- Cancelar ----

    [Fact]
    public async Task Cancelar_con_sesion_abierta_del_ejecutor_reversa_en_su_sesion()
    {
        using var db = NewDb();
        var (_, sesion) = await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(1000m);
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();
        var cobro = await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 1000m)]), CancellationToken.None);

        var eventos = new FakeIntegrationEventPublisher();
        await CancelarHandler(db, eventos).Handle(
            new CancelarCobroMostradorCommand(cobro.Id, "Cliente devolvió"), CancellationToken.None);

        (await db.CobrosMostrador.SingleAsync()).Estado.Should().Be(EstadoCobroMostrador.Cancelado);
        factura.CajaId.Should().BeNull(); // libera para re-cobro

        var reversa = await db.CajaMovimientos.SingleAsync(m => m.Tipo == TipoCajaMovimiento.ReversaCobro);
        reversa.CajaSesionId.Should().Be(sesion.Id);
        reversa.Importe.Should().Be(-1000m);
        (await db.CajaAjustesPendientes.CountAsync()).Should().Be(0);

        eventos.Publicados.OfType<CobroMostradorCanceladoIntegrationEvent>()
            .Single().ReversadoEnSesion.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelar_sin_sesion_del_ejecutor_genera_ajuste_pendiente_de_la_caja()
    {
        using var db = NewDb();
        var (caja, _) = await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(300m);
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();
        var cobro = await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 300m)]), CancellationToken.None);

        // Cancela un perfil administrativo SIN sesión abierta.
        var eventos = new FakeIntegrationEventPublisher();
        await CancelarHandler(db, eventos, usuario: Guid.NewGuid()).Handle(
            new CancelarCobroMostradorCommand(cobro.Id, "Cobro duplicado"), CancellationToken.None);

        var ajuste = await db.CajaAjustesPendientes.SingleAsync();
        ajuste.CajaId.Should().Be(caja.Id);
        ajuste.Importe.Should().Be(-300m);
        ajuste.CobroMostradorId.Should().Be(cobro.Id);
        ajuste.AplicadoEnSesionId.Should().BeNull();

        (await db.CajaMovimientos.CountAsync(m => m.Tipo == TipoCajaMovimiento.ReversaCobro)).Should().Be(0);
        eventos.Publicados.OfType<CobroMostradorCanceladoIntegrationEvent>()
            .Single().ReversadoEnSesion.Should().BeFalse();
    }

    [Fact]
    public async Task Tras_cancelar_el_comprobante_admite_re_cobro()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var factura = FacturaTimbrada(200m);
        db.FacturasVenta.Add(factura);
        await db.SaveChangesAsync();

        var primero = await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("01", 200m)]), CancellationToken.None);
        await CancelarHandler(db).Handle(
            new CancelarCobroMostradorCommand(primero.Id, "Error de captura"), CancellationToken.None);

        var segundo = await RegistrarHandler(db).Handle(new RegistrarCobroMostradorCommand(
            factura.Id, [new CobroFormaPagoInput("04", 200m)]), CancellationToken.None);

        segundo.Estado.Should().Be("Registrado");
        (await db.CobrosMostrador.CountAsync()).Should().Be(2);
    }

    // ---- Dominio ----

    [Fact]
    public void AsignarCajaCobro_es_la_unica_via_y_rechaza_doble_asignacion()
    {
        var factura = FacturaTimbrada(100m);
        var cajaA = Guid.NewGuid();

        factura.AsignarCajaCobro(cajaA);
        factura.CajaId.Should().Be(cajaA);
        factura.AsignarCajaCobro(cajaA); // idempotente

        var otra = () => factura.AsignarCajaCobro(Guid.NewGuid());
        otra.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMPROBANTE_CAJA_YA_ASIGNADA");

        factura.DesasignarCajaCobro();
        factura.CajaId.Should().BeNull();
    }

    [Fact]
    public void Registrar_exige_formas_de_pago_validas()
    {
        var sinFormas = () => CobroMostrador.Registrar(
            Empresa, Guid.NewGuid(), SucursalId, 1, Guid.NewGuid(),
            OrigenCobroMostrador.Mostrador, Ahora, Cajero, []);
        sinFormas.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COBRO_SIN_FORMAS_PAGO");

        var importeCero = () => CobroMostrador.Registrar(
            Empresa, Guid.NewGuid(), SucursalId, 1, Guid.NewGuid(),
            OrigenCobroMostrador.Mostrador, Ahora, Cajero, [("01", 0m, null, null, null)]);
        importeCero.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COBRO_IMPORTE_INVALIDO");
    }
}
