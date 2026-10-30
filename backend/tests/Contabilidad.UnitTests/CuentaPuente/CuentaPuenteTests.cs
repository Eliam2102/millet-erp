using Millet.Contabilidad.Application.CuentaPuente;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests.CuentaPuente;

/// <summary>
/// K10.5: cuenta puente recepción–factura. Saldo y naturaleza por OC, antigüedad desde el primer movimiento sin saldar, tramos,
/// reversa por anulación de póliza, idempotencia y resolución de la OC de una nota de cargo por la factura de origen.
/// Montos sin IVA, en MXN. Recepción = abono a la puente; factura = cargo.
/// </summary>
public class CuentaPuenteTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Cuenta = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Proveedor = Guid.NewGuid();
    private static readonly DateOnly Corte = new(2026, 10, 31);

    private static MovimientoPuente Mov(Guid oc, DateOnly fecha, decimal cargo, decimal abono,
        TipoDocumentoPuente tipo = TipoDocumentoPuente.Recepcion, Guid? documento = null, string moneda = "MXN", decimal tc = 1m, decimal? original = null) =>
        MovimientoPuente.Registrar(Guid.NewGuid(), Empresa, Guid.NewGuid(), Guid.NewGuid(), "TT-000001", fecha, Cuenta, Sucursal, Proveedor,
            oc, "OC-DEMO-K105", null, tipo, documento ?? Guid.NewGuid(), null, cargo, abono, moneda, tc, original);

    private static PartidaPuente Partida(decimal cargo, decimal abono, ReferenciaPuente? referencia, Guid? partidaId = null, string? folio = null) =>
        new(partidaId ?? Guid.NewGuid(), 1, Cuenta, cargo, abono, Sucursal, Proveedor, "MXN", 1m, null, referencia, folio);

    // ── Saldo, naturaleza y tramos ─────────────────────────────────────

    [Fact]
    public void Criterio_a_recepcion_y_factura_por_el_mismo_importe_dejan_la_OC_saldada()
    {
        var oc = Guid.NewGuid();
        var r = ConciliacionPuente.Calcular(
            [Mov(oc, new(2026, 10, 1), 0m, 10_000m, TipoDocumentoPuente.Recepcion), Mov(oc, new(2026, 10, 6), 10_000m, 0m, TipoDocumentoPuente.Factura)], Corte);

        r.Saldo.Should().Be(0m);
        r.Naturaleza.Should().Be(NaturalezaSaldoPuente.Saldada);
        r.FechaPrimerMovimientoSinSaldar.Should().BeNull();
        r.DiasAntiguedad.Should().BeNull();
        r.Tramo.Should().BeNull();
    }

    [Fact]
    public void Criterio_b_factura_sin_recepcion_es_saldo_deudor_facturado_sin_recibir_y_al_recibir_queda_en_cero()
    {
        var oc = Guid.NewGuid();
        var factura = Mov(oc, new(2026, 10, 10), 5_000m, 0m, TipoDocumentoPuente.Factura);

        var antes = ConciliacionPuente.Calcular([factura], Corte);
        antes.Saldo.Should().Be(5_000m);
        antes.Naturaleza.Should().Be(NaturalezaSaldoPuente.FacturadoSinRecibir);
        antes.FechaPrimerMovimientoSinSaldar.Should().Be(new DateOnly(2026, 10, 10));
        antes.DiasAntiguedad.Should().Be(21);
        antes.Tramo.Should().Be(TramoAntiguedadPuente.De0a30);

        var despues = ConciliacionPuente.Calcular([factura, Mov(oc, new(2026, 10, 20), 0m, 5_000m, TipoDocumentoPuente.Recepcion)], Corte);
        despues.Saldo.Should().Be(0m);
        despues.Naturaleza.Should().Be(NaturalezaSaldoPuente.Saldada);
    }

    [Fact]
    public void La_recepcion_sin_factura_es_recibido_sin_facturar()
    {
        var r = ConciliacionPuente.Calcular([Mov(Guid.NewGuid(), new(2026, 9, 15), 0m, 3_000m, TipoDocumentoPuente.Recepcion)], Corte);
        r.Saldo.Should().Be(-3_000m);
        r.Naturaleza.Should().Be(NaturalezaSaldoPuente.RecibidoSinFacturar);
        r.DiasAntiguedad.Should().Be(46);
        r.Tramo.Should().Be(TramoAntiguedadPuente.De31a60);
    }

    [Fact]
    public void Criterio_c_saldo_sin_movimiento_hace_61_dias_cae_en_mas_de_60()
    {
        var r = ConciliacionPuente.Calcular([Mov(Guid.NewGuid(), new(2026, 8, 31), 0m, 1_000m)], Corte);
        r.DiasAntiguedad.Should().Be(61);
        r.Tramo.Should().Be(TramoAntiguedadPuente.MasDe60);
        ConciliacionPuente.Tramo(30).Should().Be(TramoAntiguedadPuente.De0a30);
        ConciliacionPuente.Tramo(31).Should().Be(TramoAntiguedadPuente.De31a60);
        ConciliacionPuente.Tramo(60).Should().Be(TramoAntiguedadPuente.De31a60);
    }

    [Fact]
    public void La_antiguedad_cuenta_desde_el_primer_movimiento_despues_de_la_ultima_vez_que_la_OC_quedo_en_cero()
    {
        var oc = Guid.NewGuid();
        // Saldada el 10-ago y reabierta el 15-sep con una recepción parcial; luego una factura parcial que no la salda.
        var movs = new[]
        {
            Mov(oc, new(2026, 8, 1), 0m, 2_000m, TipoDocumentoPuente.Recepcion),
            Mov(oc, new(2026, 8, 10), 2_000m, 0m, TipoDocumentoPuente.Factura),
            Mov(oc, new(2026, 9, 15), 0m, 4_000m, TipoDocumentoPuente.Recepcion),
            Mov(oc, new(2026, 10, 5), 1_000m, 0m, TipoDocumentoPuente.Factura),
        };
        var r = ConciliacionPuente.Calcular(movs, Corte);
        r.Saldo.Should().Be(-3_000m);
        r.FechaPrimerMovimientoSinSaldar.Should().Be(new DateOnly(2026, 9, 15));
        r.DiasAntiguedad.Should().Be(46);

        // Si nunca llegó a cero, cuenta desde el primero.
        ConciliacionPuente.Calcular(movs.Skip(2), Corte).FechaPrimerMovimientoSinSaldar.Should().Be(new DateOnly(2026, 9, 15));
    }

    [Fact]
    public void El_corte_ignora_los_movimientos_posteriores()
    {
        var oc = Guid.NewGuid();
        var r = ConciliacionPuente.Calcular(
            [Mov(oc, new(2026, 10, 1), 0m, 10_000m), Mov(oc, new(2026, 11, 3), 10_000m, 0m)], Corte);
        r.Saldo.Should().Be(-10_000m);
        r.Abonos.Should().Be(10_000m);
        r.Cargos.Should().Be(0m);
    }

    [Fact]
    public void Criterio_d_recepcion_factura_devolucion_y_nota_de_cargo_dejan_la_OC_en_cero()
    {
        var oc = Guid.NewGuid();
        var r = ConciliacionPuente.Calcular(
        [
            Mov(oc, new(2026, 10, 1), 0m, 10_000m, TipoDocumentoPuente.Recepcion),
            Mov(oc, new(2026, 10, 6), 10_000m, 0m, TipoDocumentoPuente.Factura),
            Mov(oc, new(2026, 10, 12), 2_000m, 0m, TipoDocumentoPuente.Devolucion),
            Mov(oc, new(2026, 10, 14), 0m, 2_000m, TipoDocumentoPuente.NotaCargo),
        ], Corte);
        r.Saldo.Should().Be(0m);
        r.Naturaleza.Should().Be(NaturalezaSaldoPuente.Saldada);
    }

    [Fact]
    public void La_diferencia_cambiaria_es_el_residuo_en_pesos_cuando_la_OC_quedo_saldada_en_moneda_original()
    {
        var oc = Guid.NewGuid();
        var r = ConciliacionPuente.Calcular(
        [
            Mov(oc, new(2026, 10, 1), 0m, 18_500m, moneda: "USD", tc: 18.5m, original: 1_000m),
            Mov(oc, new(2026, 10, 6), 19_000m, 0m, moneda: "USD", tc: 19m, original: 1_000m),
        ], Corte);
        r.Saldo.Should().Be(500m);
        r.DiferenciaCambiaria.Should().Be(500m);

        // En pesos o con saldo en la moneda original no aplica.
        ConciliacionPuente.Calcular([Mov(oc, new(2026, 10, 1), 0m, 100m)], Corte).DiferenciaCambiaria.Should().BeNull();
        ConciliacionPuente.Calcular([Mov(oc, new(2026, 10, 1), 0m, 18_500m, moneda: "USD", tc: 18.5m, original: 1_000m)], Corte)
            .DiferenciaCambiaria.Should().BeNull();
    }

    // ── Movimiento: validación y reversa ──────────────────────────────

    [Fact]
    public void El_movimiento_lleva_un_solo_lado_positivo_y_la_OC()
    {
        ((Action)(() => Mov(Guid.NewGuid(), Corte, 10m, 10m))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PUENTE_IMPORTE_INVALIDO");
        ((Action)(() => Mov(Guid.NewGuid(), Corte, 0m, 0m))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PUENTE_IMPORTE_INVALIDO");
        ((Action)(() => Mov(Guid.NewGuid(), Corte, -5m, 0m))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PUENTE_IMPORTE_INVALIDO");
        ((Action)(() => Mov(Guid.Empty, Corte, 5m, 0m))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PUENTE_OC_NO_RESUELTA");
    }

    [Fact]
    public void Criterio_e_la_reversa_por_anulacion_invierte_el_lado_y_la_OC_regresa()
    {
        var oc = Guid.NewGuid();
        var recepcion = Mov(oc, new(2026, 10, 1), 0m, 10_000m, TipoDocumentoPuente.Recepcion);
        var anulacion = Guid.NewGuid();

        var reversas = PlaneadorMovimientosPuente.PlanearReversas([recepcion], new HashSet<Guid>(), anulacion, "TT-000009", new DateOnly(2026, 10, 3));

        var reversa = reversas.Should().ContainSingle().Subject;
        reversa.EsReversa.Should().BeTrue();
        reversa.MovimientoRevertidoId.Should().Be(recepcion.Id);
        reversa.PolizaId.Should().Be(anulacion);
        reversa.PolizaPartidaId.Should().Be(recepcion.PolizaPartidaId);
        reversa.PolizaFolio.Should().Be("TT-000009");
        reversa.Fecha.Should().Be(new DateOnly(2026, 10, 3));
        reversa.Cargo.Should().Be(10_000m);
        reversa.Abono.Should().Be(0m);
        reversa.OrdenCompraId.Should().Be(oc);
        reversa.TipoDocumento.Should().Be(TipoDocumentoPuente.Recepcion);
        ConciliacionPuente.Calcular([recepcion, reversa], Corte).Saldo.Should().Be(0m);

        // Idempotente: la partida ya revertida no se revierte otra vez; una reversa no se revierte.
        PlaneadorMovimientosPuente.PlanearReversas([recepcion], new HashSet<Guid> { recepcion.PolizaPartidaId }, anulacion, null, Corte)
            .Should().BeEmpty();
        PlaneadorMovimientosPuente.PlanearReversas([reversa], new HashSet<Guid>(), Guid.NewGuid(), null, Corte).Should().BeEmpty();
    }

    // ── Planeador de entrada ──────────────────────────────────────────

    [Fact]
    public void Solo_las_partidas_con_referencia_generan_movimiento_y_repetir_no_duplica()
    {
        var oc = Guid.NewGuid();
        var recepcion = Guid.NewGuid();
        var conRef = Partida(0m, 10_000m, new ReferenciaPuente(oc, null, TipoDocumentoPuente.Recepcion, recepcion), folio: "OC-DEMO-K105-01");
        var sinRef = Partida(10_000m, 0m, null);
        var poliza = Guid.NewGuid();

        var nuevos = PlaneadorMovimientosPuente.Planear(Empresa, poliza, "EM-000001", new DateOnly(2026, 10, 1), [conRef, sinRef],
            new HashSet<Guid>(), _ => null);

        var m = nuevos.Should().ContainSingle().Subject;
        m.EmpresaId.Should().Be(Empresa);
        m.PolizaId.Should().Be(poliza);
        m.PolizaPartidaId.Should().Be(conRef.PolizaPartidaId);
        m.PolizaFolio.Should().Be("EM-000001");
        m.OrdenCompraId.Should().Be(oc);
        m.OrdenCompraFolio.Should().Be("OC-DEMO-K105-01");
        m.DocumentoId.Should().Be(recepcion);
        m.Abono.Should().Be(10_000m);
        m.EsReversa.Should().BeFalse();

        PlaneadorMovimientosPuente.Planear(Empresa, poliza, "EM-000001", new DateOnly(2026, 10, 1), [conRef, sinRef],
            new HashSet<Guid> { conRef.PolizaPartidaId }, _ => null).Should().BeEmpty();
    }

    [Fact]
    public void Una_partida_en_negativo_se_registra_del_lado_contrario_en_positivo()
    {
        var p = Partida(-2_000m, 0m, new ReferenciaPuente(Guid.NewGuid(), null, TipoDocumentoPuente.Devolucion, Guid.NewGuid(), null));
        var m = PlaneadorMovimientosPuente.Planear(Empresa, Guid.NewGuid(), null, Corte, [p], new HashSet<Guid>(), _ => null).Single();
        m.Cargo.Should().Be(0m);
        m.Abono.Should().Be(2_000m);
    }

    [Fact]
    public void La_nota_de_cargo_sin_OC_toma_la_OC_de_su_factura_de_origen()
    {
        var oc = Guid.NewGuid();
        var factura = Guid.NewGuid();
        var nota = Partida(0m, 2_000m, new ReferenciaPuente(Guid.Empty, null, TipoDocumentoPuente.NotaCargo, Guid.NewGuid(), factura));

        var m = PlaneadorMovimientosPuente.Planear(Empresa, Guid.NewGuid(), null, Corte, [nota], new HashSet<Guid>(),
            f => f == factura ? new OcResueltaPuente(oc, "OC-DEMO-K105-04", Proveedor) : null).Single();

        m.OrdenCompraId.Should().Be(oc);
        m.OrdenCompraFolio.Should().Be("OC-DEMO-K105-04");
        m.FacturaOrigenId.Should().Be(factura);
    }

    [Fact]
    public void La_factura_de_origen_puede_venir_en_la_misma_poliza()
    {
        var oc = Guid.NewGuid();
        var factura = Guid.NewGuid();
        var pf = Partida(5_000m, 0m, new ReferenciaPuente(oc, null, TipoDocumentoPuente.Factura, factura), folio: "OC-X");
        var pn = Partida(0m, 1_000m, new ReferenciaPuente(Guid.Empty, null, TipoDocumentoPuente.NotaCredito, Guid.NewGuid(), factura));

        var nuevos = PlaneadorMovimientosPuente.Planear(Empresa, Guid.NewGuid(), null, Corte, [pf, pn], new HashSet<Guid>(), _ => null);
        nuevos.Should().HaveCount(2).And.OnlyContain(x => x.OrdenCompraId == oc);
    }

    [Fact]
    public void Sin_OC_ni_factura_de_origen_conocida_falla_con_PUENTE_OC_NO_RESUELTA()
    {
        var sinFactura = Partida(0m, 1m, new ReferenciaPuente(Guid.Empty, null, TipoDocumentoPuente.NotaCargo, Guid.NewGuid(), null));
        var facturaDesconocida = Partida(0m, 1m, new ReferenciaPuente(Guid.Empty, null, TipoDocumentoPuente.NotaCargo, Guid.NewGuid(), Guid.NewGuid()));

        foreach (var p in new[] { sinFactura, facturaDesconocida })
            ((Action)(() => PlaneadorMovimientosPuente.Planear(Empresa, Guid.NewGuid(), null, Corte, [p], new HashSet<Guid>(), _ => null)))
                .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PUENTE_OC_NO_RESUELTA");
    }
}
