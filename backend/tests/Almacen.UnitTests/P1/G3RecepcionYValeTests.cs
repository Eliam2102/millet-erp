using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.P1;

/// <summary>G-3: recepción sin duplicar (remisión/factura), lista de empaque sin vidrio y vale regularizado contra el saldo de la RQ.</summary>
public sealed class G3RecepcionYValeTests
{
    private static RegistrarRecepcionLineaInput Linea(P1Fixture f) => new(f.ArticuloId, f.LineaId, 1, null, null, f.BinId);

    private static Task<RegistrarRecepcionResponse> ConFactura(P1Fixture f, Guid cfdi, string? remision, string? motivo = null) =>
        new RegistrarRecepcionConFacturaHandler(f.Db, f.Oc, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true),
            new P7Support.Conversion(), CfdiFake.Valido(), new ProveedorRfcFake())
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, cfdi, null, null, [Linea(f)], Remision: remision, MotivoDuplicado: motivo), default);

    private static Task<RegistrarRecepcionResponse> ConPackingList(P1Fixture f, string? remision, string? motivo = null, IComprasOcReadPort? oc = null) =>
        new RegistrarRecepcionConPackingListHandler(f.Db, oc ?? f.Oc, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion())
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, "packing.pdf", null, [Linea(f)], Remision: remision, MotivoDuplicado: motivo), default);

    [Fact]
    public async Task Criterio1_misma_remision_en_la_OC_avisa_ya_recibida_y_solo_continua_con_motivo()
    {
        await using var f = new P1Fixture();
        await ConFactura(f, Guid.NewGuid(), "R-55");

        var ex = await Assert.ThrowsAnyAsync<BusinessRuleException>(() => ConFactura(f, Guid.NewGuid(), " r-55 "));
        ex.Code.Should().Be("RECEPCION_YA_RECIBIDA");
        ex.Message.Should().Contain("ya fue recibida");
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
        f.Events.Items.OfType<OcRecepcionRegistradaIntegrationEvent>().Should().ContainSingle();

        await ConFactura(f, Guid.NewGuid(), "R-55", "Entrega complementaria del mismo embarque");
        var movs = await f.Db.Movimientos.ToListAsync();
        movs.Should().HaveCount(2);
        movs.Should().OnlyContain(m => m.Remision == "R-55");
        movs.Count(m => m.MotivoDuplicado == "Entrega complementaria del mismo embarque").Should().Be(1);
    }

    [Fact]
    public async Task N11_referencia_duplicada_sin_motivo_abre_incidencia_de_la_OC_y_no_crea_entrada()
    {
        await using var f = new P1Fixture();
        var cfdi = Guid.NewGuid();
        var primera = await ConFactura(f, cfdi, "R-90");

        var ex = await Assert.ThrowsAsync<Millet.SharedKernel.Application.Incidencias.IncidenciaRechazoException>(() => ConFactura(f, cfdi, "R-91"));

        ex.Code.Should().Be("RECEPCION_YA_RECIBIDA");
        ex.Solicitud.DocumentoId.Should().Be(f.DocumentoId);
        ex.Solicitud.DetalleClave.Should().Be(Millet.Almacen.Application.Incidencias.IncidenciasAlmacen.ClaveDuplicada);
        ex.Solicitud.Detalle.Should().Contain(primera.Folio).And.Contain("factura");
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Misma_remision_por_lista_de_empaque_tambien_pide_motivo()
    {
        await using var f = new P1Fixture();
        await ConPackingList(f, "R-77");

        var ex = await Assert.ThrowsAnyAsync<BusinessRuleException>(() => ConPackingList(f, "R-77"));
        ex.Code.Should().Be("RECEPCION_YA_RECIBIDA");
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
        await ConPackingList(f, "R-77", "Segundo camión");
        (await f.Db.Movimientos.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Misma_factura_en_la_OC_pide_motivo_aunque_la_remision_sea_otra()
    {
        await using var f = new P1Fixture();
        var cfdi = Guid.NewGuid();
        await ConFactura(f, cfdi, "R-1");

        var ex = await Assert.ThrowsAnyAsync<BusinessRuleException>(() => ConFactura(f, cfdi, "R-2"));
        ex.Code.Should().Be("RECEPCION_YA_RECIBIDA");
    }

    private static Task<RegistrarRecepcionResponse> ConCanal(P1Fixture f, CfdiFake cxp, Guid? cfdi, string? uuid, string? motivo = null) =>
        new RegistrarRecepcionConFacturaHandler(f.Db, f.Oc, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true),
            new P7Support.Conversion(), cxp, new ProveedorRfcFake())
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, cfdi, uuid, null, [Linea(f)], MotivoDuplicado: motivo), default);

    [Fact]
    public async Task N11_misma_factura_por_CFDI_del_sistema_y_luego_por_folio_fiscal_impreso_pide_motivo()
    {
        await using var f = new P1Fixture();
        var cxp = CfdiFake.Valido();
        var cfdi = Guid.NewGuid();
        cxp.Uuids[cfdi] = "69d2595a-3114-46af-a37f-f854d713c418";
        var primera = await ConCanal(f, cxp, cfdi, null);

        var ex = await Assert.ThrowsAsync<Millet.SharedKernel.Application.Incidencias.IncidenciaRechazoException>(
            () => ConCanal(f, cxp, null, "69D2595A-3114-46AF-A37F-F854D713C418"));

        ex.Code.Should().Be("RECEPCION_YA_RECIBIDA");
        ex.Solicitud.Detalle.Should().Contain(primera.Folio);
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
        await ConCanal(f, cxp, null, "69D2595A-3114-46AF-A37F-F854D713C418", "Factura impresa de la segunda entrega");
        (await f.Db.Movimientos.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task N11_misma_factura_por_folio_fiscal_impreso_y_luego_por_CFDI_del_sistema_pide_motivo()
    {
        await using var f = new P1Fixture();
        var cxp = CfdiFake.Valido();
        var cfdi = Guid.NewGuid();
        cxp.Uuids[cfdi] = "ECCC0A95-704C-4BFD-90D4-D779611D27E8";
        await ConCanal(f, cxp, null, "eccc0a95-704c-4bfd-90d4-d779611d27e8");

        var ex = await Assert.ThrowsAnyAsync<BusinessRuleException>(() => ConCanal(f, cxp, cfdi, null));

        ex.Code.Should().Be("RECEPCION_YA_RECIBIDA");
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Otra_remision_sin_remision_o_recepcion_cancelada_no_pide_motivo()
    {
        await using var f = new P1Fixture();
        var primera = await ConFactura(f, Guid.NewGuid(), "R-10");
        await ConFactura(f, Guid.NewGuid(), "R-11");
        await ConFactura(f, Guid.NewGuid(), null);
        (await f.Db.Movimientos.CountAsync()).Should().Be(3);

        (await f.Db.Movimientos.SingleAsync(m => m.Id == primera.RecepcionId)).CancelarRecepcion("Captura equivocada");
        await f.Db.SaveChangesAsync();
        await ConFactura(f, Guid.NewGuid(), "R-10");
        (await f.Db.Movimientos.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Criterio2_lista_de_empaque_rechaza_la_partida_de_vidrio_sin_registrar_nada()
    {
        await using var f = new P1Fixture();
        var oc = await f.Oc.ObtenerAsync(f.DocumentoId, default);
        var conVidrio = new P1Fixture.OcPort(oc! with { Lineas = [oc.Lineas[0] with { ProductoAwVarianteId = Guid.NewGuid(), AnchoMm = 2440m, AltoMm = 3660m }] });

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => ConPackingList(f, null, oc: conVidrio));

        ex.Code.Should().Be("LISTA_EMPAQUE_VIDRIO");
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        f.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Criterio3_vale_de_5_contra_RQ_de_8_descuenta_5_del_saldo_y_avisa_a_Compras_una_sola_vez()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale, 5);
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var rq8 = new P1Fixture.RqPort(rq with { Lineas = [rq.Lineas[0] with { CantidadSolicitada = 8, CantidadDisponibleEntregar = 8 }] });
        var handler = new RegularizarSalidaPorValeHandler(f.Db, rq8, f.Events);

        await handler.Handle(new(vale.Id, f.DocumentoId), default);

        var e = f.Events.Items.OfType<ValeRegularizadoIntegrationEvent>().Should().ContainSingle().Subject;
        e.RqId.Should().Be(f.DocumentoId);
        e.SalidaId.Should().Be(vale.Id);
        var linea = e.Lineas.Should().ContainSingle().Subject;
        linea.LineaRqId.Should().Be(f.LineaId);
        linea.Cantidad.Should().Be(5m);
        // Saldo de la RQ tras aplicar el evento en Compras: 8 - 5 = 3.
        (8m - e.Lineas.Sum(l => l.Cantidad)).Should().Be(3m);

        // Repetir la regularización no vuelve a descontar ni a avisar.
        await Assert.ThrowsAnyAsync<BusinessRuleException>(() => handler.Handle(new(vale.Id, f.DocumentoId), default));
        f.Events.Items.OfType<ValeRegularizadoIntegrationEvent>().Should().ContainSingle();
    }
}
