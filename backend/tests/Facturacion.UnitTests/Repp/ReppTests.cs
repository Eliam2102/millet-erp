using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Application.Repp.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Repp;

public sealed class ReppTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    // ---- Dominio: diferencia cambiaria ----

    [Fact]
    public void DiferenciaCambiaria_es_cero_en_moneda_nacional()
    {
        ReciboPago.CalcularDiferenciaCambiaria("MXN", 1000m, null, null).Should().Be(0m);
    }

    [Fact]
    public void DiferenciaCambiaria_ganancia_si_el_TC_sube()
    {
        // 100 USD pagados, TC factura 20, TC pago 21 → ganancia 100.
        ReciboPago.CalcularDiferenciaCambiaria("USD", 100m, 20m, 21m).Should().Be(100m);
    }

    [Fact]
    public void DiferenciaCambiaria_perdida_si_el_TC_baja()
    {
        ReciboPago.CalcularDiferenciaCambiaria("USD", 100m, 20m, 19.5m).Should().Be(-50m);
    }

    // ---- Dominio: agregar factura pagada ----

    private static ReciboPago NuevoRepp() => ReciboPago.CrearBorrador(
        Guid.NewGuid(), "P-1", 1, Guid.NewGuid(), null, null,
        new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
        new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), 2026, 5, Ahora, "MXN");

    [Fact]
    public void AgregarFacturaPagada_calcula_saldo_insoluto()
    {
        var repp = NuevoRepp();

        var linea = repp.AgregarFacturaPagada(Guid.NewGuid(), "UUID-1", 1, "MXN", 400m, 1000m, "03", null, null, null, null, null, "01", 1000m, []);

        linea.SaldoInsoluto.Should().Be(600m);
        linea.GananciaPerdidaCambiaria.Should().Be(0m);
    }

    /// <summary>
    /// ImpuestosDR (Pago 2.0): la base gravable de la factura se prorratea al
    /// importe pagado — factor = importePagado / totalFactura. Factura base 1000
    /// + IVA 16% = 1160; pago 580 (mitad) → baseDR 500, importeDR 80.
    /// </summary>
    [Fact]
    public void AgregarFacturaPagada_prorratea_impuestos_dr()
    {
        var repp = NuevoRepp();

        var linea = repp.AgregarFacturaPagada(
            Guid.NewGuid(), "UUID-1", 1, "MXN", 580m, 1160m, "03", null, null, null, null, null,
            "02", 1160m, [new ImpuestoFacturaPagada("002", "Tasa", 0.16m, EsRetencion: false, BaseGravable: 1000m)]);

        linea.ObjetoImpDR.Should().Be("02");
        linea.Equivalencia.Should().Be(1m);
        linea.BaseGravablePagada.Should().Be(500m);
        var imp = linea.Impuestos.Single();
        imp.Impuesto.Should().Be("002");
        imp.TipoFactor.Should().Be("Tasa");
        imp.TasaOCuota.Should().Be(0.16m);
        imp.EsRetencion.Should().BeFalse();
        imp.BaseDR.Should().Be(500m);
        imp.ImporteDR.Should().Be(80m);
    }

    [Fact]
    public void AgregarFacturaPagada_sobrepago_lanza()
    {
        var repp = NuevoRepp();
        var act = () => repp.AgregarFacturaPagada(Guid.NewGuid(), "UUID-1", 1, "MXN", 1500m, 1000m, "03", null, null, null, null, null, "01", 1000m, []);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "REPP_SOBREPAGO");
    }

    [Fact]
    public void AgregarFacturaPagada_forma_99_lanza()
    {
        var repp = NuevoRepp();
        var act = () => repp.AgregarFacturaPagada(Guid.NewGuid(), "UUID-1", 1, "MXN", 400m, 1000m, "99", null, null, null, null, null, "01", 1000m, []);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "REPP_FORMA_PAGO_INVALIDA");
    }

    // ---- Handler ----

    private static FacturaVenta FacturaPpd(Guid empresaId, string moneda = "MXN", decimal? tc = null, decimal valor = 1000m)
    {
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PPD", "99", moneda, tc, 2026, 5,
            (short)7, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Servicio", "E48", 1m, valor, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        return fv;
    }

    private static EmitirReppHandler Handler(FacturacionDbContext db, Guid empresaId) =>
        new(db, new FakeSender(new ReservarFolioResponse("P-000001", 1, "")),
            new FakePeriodoContablePort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    [Fact]
    public async Task Emitir_REPP_cubre_varias_facturas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f1 = FacturaPpd(empresaId);
        var f2 = FacturaPpd(empresaId);
        db.FacturasVenta.AddRange(f1, f2);
        await db.SaveChangesAsync();

        var resp = await Handler(db, empresaId).Handle(new EmitirReppCommand(
            Guid.NewGuid(), Ahora, "MXN", null, "03", null, null, "REF-1",
            [new ReppFacturaPago(f1.Id, 400m), new ReppFacturaPago(f2.Id, 1000m)]), CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Facturas.Should().HaveCount(2);
        resp.ImporteTotalPago.Should().Be(1400m);
        resp.Facturas.Should().OnlyContain(f => f.NumParcialidad == 1);
        (await db.RecibosPago.SingleAsync()).Tipo.Should().Be(TipoComprobante.Pago);
    }

    [Fact]
    public async Task Emitir_REPP_calcula_ganancia_cambiaria_USD()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f = FacturaPpd(empresaId, moneda: "USD", tc: 20m, valor: 100m);
        db.FacturasVenta.Add(f);
        await db.SaveChangesAsync();

        var resp = await Handler(db, empresaId).Handle(new EmitirReppCommand(
            Guid.NewGuid(), Ahora, "USD", 21m, "03", null, null, null,
            [new ReppFacturaPago(f.Id, 100m)]), CancellationToken.None);

        resp.GananciaPerdidaCambiariaTotal.Should().Be(100m);
        resp.Facturas[0].SaldoInsoluto.Should().Be(0m);
    }

    // ---- [Decisión 13-K]: NC timbradas acreditan al saldo del complemento ----

    private static NotaCredito NcAmortizacionTimbrada(Guid empresaId, Guid facturaId, decimal monto)
    {
        var nc = NotaCredito.CrearAmortizacion(empresaId, "NC-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"),
            "01", "MXN", null, 2026, 5, 1, Guid.NewGuid(), facturaId, monto, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return nc;
    }

    [Fact]
    public async Task Emitir_REPP_resta_NC_de_amortizacion_del_saldo()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f = FacturaPpd(empresaId); // total 1000
        db.FacturasVenta.Add(f);
        db.NotasCredito.Add(NcAmortizacionTimbrada(empresaId, f.Id, 400m));
        await db.SaveChangesAsync();

        var resp = await Handler(db, empresaId).Handle(new EmitirReppCommand(
            Guid.NewGuid(), Ahora, "MXN", null, "03", null, null, null,
            [new ReppFacturaPago(f.Id, 600m)]), CancellationToken.None);

        // Saldo anterior 600 (1000 − 400 acreditados) → saldo insoluto 0.
        resp.Facturas[0].SaldoInsoluto.Should().Be(0m);
    }

    [Fact]
    public async Task Emitir_REPP_que_excede_el_saldo_neto_lanza()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f = FacturaPpd(empresaId); // total 1000
        db.FacturasVenta.Add(f);
        db.NotasCredito.Add(NcAmortizacionTimbrada(empresaId, f.Id, 400m));
        await db.SaveChangesAsync();

        // 700 cabía en el total bruto, pero excede el saldo neto (600).
        var act = () => Handler(db, empresaId).Handle(new EmitirReppCommand(
            Guid.NewGuid(), Ahora, "MXN", null, "03", null, null, null,
            [new ReppFacturaPago(f.Id, 700m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "REPP_SOBREPAGO");
    }

    [Fact]
    public async Task Emitir_REPP_sobre_factura_totalmente_acreditada_lanza()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f = FacturaPpd(empresaId); // total 1000
        db.FacturasVenta.Add(f);
        db.NotasCredito.Add(NcAmortizacionTimbrada(empresaId, f.Id, 1000m));
        await db.SaveChangesAsync();

        var act = () => Handler(db, empresaId).Handle(new EmitirReppCommand(
            Guid.NewGuid(), Ahora, "MXN", null, "03", null, null, null,
            [new ReppFacturaPago(f.Id, 1m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "REPP_FACTURA_SIN_SALDO");
    }

    // ---- Facturas cobrables PPD (FacturaPpdPicker) ----

    /// <summary>REPP previo (Borrador basta — vigente) que cubre una factura.</summary>
    private static ReciboPago ReppPrevio(Guid empresaId, FacturaVenta factura, decimal importe, decimal saldoAnterior)
    {
        var repp = ReciboPago.CrearBorrador(
            empresaId, "P-PREV", 99, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), 2026, 5, Ahora, "MXN");
        repp.AgregarFacturaPagada(
            factura.Id, factura.Uuid ?? string.Empty, 1, "MXN", importe, saldoAnterior,
            "03", null, null, null, null, null, "01", saldoAnterior, []);
        repp.EstablecerImporteTotalPago(importe);
        return repp;
    }

    [Fact]
    public async Task Cobrables_PPD_calcula_saldo_con_NC_y_pagos_y_excluye_saldadas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var conSaldo = FacturaPpd(empresaId);                  // total 1000
        var saldada = FacturaPpd(empresaId, valor: 500m);      // total 500
        db.FacturasVenta.AddRange(conSaldo, saldada);
        db.NotasCredito.Add(NcAmortizacionTimbrada(empresaId, conSaldo.Id, 400m));
        db.RecibosPago.Add(ReppPrevio(empresaId, conSaldo, 100m, 600m));
        db.RecibosPago.Add(ReppPrevio(empresaId, saldada, 500m, 500m));
        await db.SaveChangesAsync();

        var items = await new FacturasCobrablesPpdHandler(db)
            .Handle(new FacturasCobrablesPpdQuery(), CancellationToken.None);

        var item = items.Should().ContainSingle().Subject;
        item.FacturaVentaId.Should().Be(conSaldo.Id);
        item.AcreditadoNc.Should().Be(400m);
        item.PagadoRepp.Should().Be(100m);
        item.Saldo.Should().Be(500m);
        item.NumParcialidadSiguiente.Should().Be(2);
    }

    [Fact]
    public async Task Cobrables_PPD_ignora_pagos_de_REPP_cancelado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f = FacturaPpd(empresaId); // total 1000
        var cancelado = ReppPrevio(empresaId, f, 1000m, 1000m);
        cancelado.MarcarTimbradoEnProceso();
        cancelado.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        cancelado.MarcarCancelacionPendiente();
        cancelado.MarcarCancelado();
        db.FacturasVenta.Add(f);
        db.RecibosPago.Add(cancelado);
        await db.SaveChangesAsync();

        var items = await new FacturasCobrablesPpdHandler(db)
            .Handle(new FacturasCobrablesPpdQuery(), CancellationToken.None);

        var item = items.Should().ContainSingle().Subject;
        item.PagadoRepp.Should().Be(0m);
        item.Saldo.Should().Be(1000m);
        item.NumParcialidadSiguiente.Should().Be(1);
    }

    [Fact]
    public async Task Cobrables_PPD_filtra_por_rfc_del_receptor()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        db.FacturasVenta.Add(FacturaPpd(empresaId)); // receptor AAA010101AAA
        await db.SaveChangesAsync();

        var deOtroCliente = await new FacturasCobrablesPpdHandler(db)
            .Handle(new FacturasCobrablesPpdQuery(ReceptorRfc: "ZZZ990101ZZ9"), CancellationToken.None);
        var delCliente = await new FacturasCobrablesPpdHandler(db)
            .Handle(new FacturasCobrablesPpdQuery(ReceptorRfc: "aaa010101aaa"), CancellationToken.None);

        deOtroCliente.Should().BeEmpty();
        delCliente.Should().ContainSingle();
    }

    [Fact]
    public async Task Emitir_REPP_ignora_pagos_de_REPP_cancelado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var f = FacturaPpd(empresaId); // total 1000
        var cancelado = ReppPrevio(empresaId, f, 1000m, 1000m);
        cancelado.MarcarTimbradoEnProceso();
        cancelado.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        cancelado.MarcarCancelacionPendiente();
        cancelado.MarcarCancelado();
        db.FacturasVenta.Add(f);
        db.RecibosPago.Add(cancelado);
        await db.SaveChangesAsync();

        // Sin el filtro de vigencia esto sería REPP_FACTURA_SIN_SALDO.
        var resp = await Handler(db, empresaId).Handle(new EmitirReppCommand(
            Guid.NewGuid(), Ahora, "MXN", null, "03", null, null, null,
            [new ReppFacturaPago(f.Id, 1000m)]), CancellationToken.None);

        resp.Facturas[0].NumParcialidad.Should().Be(1);
        resp.Facturas[0].SaldoInsoluto.Should().Be(0m);
    }

    [Fact]
    public async Task Emitir_REPP_sobre_factura_PUE_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("22222222-2222-2222-2222-222222222222", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();

        var act = () => Handler(db, empresaId).Handle(new EmitirReppCommand(
            Guid.NewGuid(), Ahora, "MXN", null, "03", null, null, null,
            [new ReppFacturaPago(fv.Id, 50m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "REPP_FACTURA_NO_PPD");
    }
}
