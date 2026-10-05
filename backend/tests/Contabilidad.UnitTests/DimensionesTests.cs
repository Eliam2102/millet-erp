using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// F1-CON-02: dominio de reglas de dimensión, resolución (herencia por rama, especificidad del tipo, vigencia) y evaluación
/// de requerimientos. Reglas de PRUEBA (FIX), no representan política de Contabilidad.
/// </summary>
public class DimensionesTests
{
    private static readonly DateOnly Dia = new(2026, 10, 15);
    private static readonly Guid Raiz = Guid.NewGuid(), Rama = Guid.NewGuid(), Hoja = Guid.NewGuid();
    private static readonly Guid TipoFactura = Guid.NewGuid(), TipoPago = Guid.NewGuid();
    private static readonly (Guid Id, string Codigo)[] Cadena = [(Hoja, "FIX-501.01.01"), (Rama, "FIX-501.01"), (Raiz, "FIX-501")];

    private static DimensionesOpciones Opc(RequerimientoDimension sinRegla = RequerimientoDimension.Opcional)
    {
        var o = new DimensionesOpciones { SinReglaEs = sinRegla };
        o.AplicarDefaults();
        return o;
    }

    private static ReglaDimension R(Guid cuenta, DimensionContable d, RequerimientoDimension req, Guid? tipo = null,
        DateOnly? desde = null, DateOnly? hasta = null) =>
        new(Guid.NewGuid(), cuenta, tipo, d, req, desde ?? new DateOnly(2026, 10, 1), hasta, esPrueba: true, nota: "FIX");

    private static RequerimientoEfectivo De(IReadOnlyList<RequerimientoEfectivo> rs, DimensionContable d) => rs.Single(r => r.Dimension == d);

    // ── Dominio ──────────────────────────────────────────────────────────────

    [Fact]
    public void Vigencia_con_fin_anterior_al_inicio_se_rechaza()
    {
        var act = () => R(Hoja, DimensionContable.Dim2, RequerimientoDimension.Obligatorio, desde: Dia, hasta: Dia.AddDays(-1));
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_REGLA_VIGENCIA_INVALIDA");
    }

    [Theory]
    [InlineData(1, null, true)]   // abierta contra abierta
    [InlineData(31, null, false)] // empieza después del cierre
    [InlineData(30, 40, true)]    // empieza el día del cierre
    public void Traslape_de_vigencias(int diaDesde, int? diaHasta, bool traslapa)
    {
        var existente = R(Hoja, DimensionContable.Dim2, RequerimientoDimension.Obligatorio, desde: new(2026, 10, 1), hasta: new(2026, 10, 30));
        var desde = new DateOnly(2026, 10, 1).AddDays(diaDesde - 1);
        DateOnly? hasta = diaHasta is { } h ? new DateOnly(2026, 10, 1).AddDays(h - 1) : null;
        existente.SeTraslapaCon(desde, hasta).Should().Be(traslapa);
    }

    [Fact]
    public void Cerrar_conserva_el_inicio_y_deja_de_estar_vigente_despues_del_fin()
    {
        var r = R(Hoja, DimensionContable.Dim3, RequerimientoDimension.Obligatorio, desde: new(2026, 10, 1));
        r.Cerrar(new(2026, 10, 20));
        r.VigenteEn(new(2026, 10, 20)).Should().BeTrue();
        r.VigenteEn(new(2026, 10, 21)).Should().BeFalse();
        r.VigenteDesde.Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void Dimension_o_requerimiento_fuera_de_catalogo_se_rechazan()
    {
        var dim = () => R(Hoja, (DimensionContable)9, RequerimientoDimension.Obligatorio);
        dim.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_REGLA_DIMENSION_INVALIDA");
        var req = () => R(Hoja, DimensionContable.Dim1, (RequerimientoDimension)0);
        req.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_REGLA_REQUERIMIENTO_INVALIDO");
    }

    // ── Resolución (D3/D4/D5) ────────────────────────────────────────────────

    [Fact]
    public void Sin_reglas_cada_dimension_toma_el_valor_configurado()
    {
        var rs = ValidadorDimensiones.Resolver(Cadena, [], TipoFactura, Dia, Opc(RequerimientoDimension.Opcional));
        rs.Should().HaveCount(3).And.OnlyContain(r => r.Requerimiento == RequerimientoDimension.Opcional && r.ReglaId == null);
        ValidadorDimensiones.Resolver(Cadena, [], TipoFactura, Dia, Opc(RequerimientoDimension.Obligatorio))
            .Should().OnlyContain(r => r.Requerimiento == RequerimientoDimension.Obligatorio);
    }

    [Fact]
    public void La_regla_de_la_rama_aplica_a_la_hoja_y_la_de_la_hoja_gana()
    {
        var deRaiz = R(Raiz, DimensionContable.Dim2, RequerimientoDimension.Obligatorio);
        var deRama = R(Rama, DimensionContable.Dim2, RequerimientoDimension.NoAplica);
        var deHoja = R(Hoja, DimensionContable.Dim3, RequerimientoDimension.Obligatorio);

        var heredada = De(ValidadorDimensiones.Resolver(Cadena, [deRaiz], null, Dia, Opc()), DimensionContable.Dim2);
        heredada.ReglaId.Should().Be(deRaiz.Id);
        heredada.Heredada.Should().BeTrue();
        heredada.CuentaOrigenCodigo.Should().Be("FIX-501");

        var rs = ValidadorDimensiones.Resolver(Cadena, [deRaiz, deRama, deHoja], null, Dia, Opc());
        De(rs, DimensionContable.Dim2).ReglaId.Should().Be(deRama.Id, "el ancestro más cercano gana");
        De(rs, DimensionContable.Dim3).Heredada.Should().BeFalse();
    }

    [Fact]
    public void A_igual_cuenta_gana_el_tipo_especifico_sobre_todos_los_tipos()
    {
        var todos = R(Hoja, DimensionContable.Dim2, RequerimientoDimension.Opcional);
        var factura = R(Hoja, DimensionContable.Dim2, RequerimientoDimension.Obligatorio, TipoFactura);

        De(ValidadorDimensiones.Resolver(Cadena, [todos, factura], TipoFactura, Dia, Opc()), DimensionContable.Dim2)
            .ReglaId.Should().Be(factura.Id);
        var pago = De(ValidadorDimensiones.Resolver(Cadena, [todos, factura], TipoPago, Dia, Opc()), DimensionContable.Dim2);
        pago.ReglaId.Should().Be(todos.Id);
        pago.ParaTodosLosTipos.Should().BeTrue();
    }

    [Fact]
    public void La_cuenta_mas_cercana_pesa_mas_que_el_tipo_especifico()
    {
        var ramaFactura = R(Rama, DimensionContable.Dim2, RequerimientoDimension.Obligatorio, TipoFactura);
        var hojaTodos = R(Hoja, DimensionContable.Dim2, RequerimientoDimension.NoAplica);
        De(ValidadorDimensiones.Resolver(Cadena, [ramaFactura, hojaTodos], TipoFactura, Dia, Opc()), DimensionContable.Dim2)
            .ReglaId.Should().Be(hojaTodos.Id);
    }

    [Fact]
    public void Se_usa_la_regla_vigente_en_la_fecha_contable_no_la_de_hoy()
    {
        var anterior = R(Hoja, DimensionContable.Dim2, RequerimientoDimension.Opcional, desde: new(2026, 10, 1), hasta: new(2026, 10, 20));
        var nueva = R(Hoja, DimensionContable.Dim2, RequerimientoDimension.Obligatorio, desde: new(2026, 10, 21));

        De(ValidadorDimensiones.Resolver(Cadena, [anterior, nueva], null, new(2026, 10, 20), Opc()), DimensionContable.Dim2)
            .ReglaId.Should().Be(anterior.Id);
        De(ValidadorDimensiones.Resolver(Cadena, [anterior, nueva], null, new(2026, 10, 21), Opc()), DimensionContable.Dim2)
            .ReglaId.Should().Be(nueva.Id);
        De(ValidadorDimensiones.Resolver(Cadena, [anterior, nueva], null, new(2026, 9, 30), Opc()), DimensionContable.Dim2)
            .ReglaId.Should().BeNull("antes de cualquier vigencia no hay regla");
    }

    [Fact]
    public void Reglas_de_otras_cuentas_no_aplican()
    {
        var otra = R(Guid.NewGuid(), DimensionContable.Dim1, RequerimientoDimension.Obligatorio);
        De(ValidadorDimensiones.Resolver(Cadena, [otra], null, Dia, Opc()), DimensionContable.Dim1).ReglaId.Should().BeNull();
    }

    // ── Evaluación ───────────────────────────────────────────────────────────

    private static MovimientoDimensionado Mov(Guid? d1 = null, Guid? d2 = null, Guid? d3 = null) =>
        new(Hoja, TipoFactura, Dia, Guid.NewGuid(), d1, d2, d3);

    private static List<ErrorDimension> Evaluar(RequerimientoDimension d1, RequerimientoDimension d2, RequerimientoDimension d3,
        MovimientoDimensionado m, CentrosEfectivos e)
    {
        var reglas = new[] { R(Hoja, DimensionContable.Dim1, d1), R(Hoja, DimensionContable.Dim2, d2), R(Hoja, DimensionContable.Dim3, d3) };
        var rs = ValidadorDimensiones.Resolver(Cadena, reglas, TipoFactura, Dia, Opc());
        return [.. ValidadorDimensiones.EvaluarRequerimientos(rs, m, e, "la cuenta FIX-501.01.01 en «FIX Factura»")];
    }

    [Fact]
    public void Combinacion_valida_con_la_dimension_obligatoria_capturada_no_tiene_errores()
    {
        var d2 = Guid.NewGuid();
        Evaluar(RequerimientoDimension.Opcional, RequerimientoDimension.Obligatorio, RequerimientoDimension.Opcional,
            Mov(d2: d2), new(Guid.NewGuid(), d2, null)).Should().BeEmpty();
    }

    [Fact]
    public void Obligatoria_ausente_nombra_la_dimension_faltante()
    {
        var errores = Evaluar(RequerimientoDimension.Opcional, RequerimientoDimension.Opcional, RequerimientoDimension.Obligatorio,
            Mov(), new(null, null, null));
        var e = errores.Should().ContainSingle().Subject;
        e.Codigo.Should().Be("CONTAB_DIM_OBLIGATORIA_FALTANTE");
        e.Campo.Should().Be("dim3Id");
        e.Dimension.Should().Be(DimensionContable.Dim3);
        e.Mensaje.Should().Contain("Dimensión 3").And.Contain("FIX-501.01.01");
    }

    [Fact]
    public void Obligatoria_superior_se_cumple_con_el_valor_derivado_de_la_jerarquia()
    {
        var d1 = Guid.NewGuid(); var d2 = Guid.NewGuid(); var d3 = Guid.NewGuid();
        Evaluar(RequerimientoDimension.Obligatorio, RequerimientoDimension.Obligatorio, RequerimientoDimension.Obligatorio,
            Mov(d3: d3), new(d1, d2, d3)).Should().BeEmpty();
    }

    [Fact]
    public void No_aplica_rechaza_lo_capturado_directo_pero_no_lo_derivado()
    {
        var d1 = Guid.NewGuid(); var d2 = Guid.NewGuid(); var d3 = Guid.NewGuid();
        Evaluar(RequerimientoDimension.Opcional, RequerimientoDimension.NoAplica, RequerimientoDimension.Opcional,
            Mov(d3: d3), new(d1, d2, d3)).Should().BeEmpty();

        var errores = Evaluar(RequerimientoDimension.Opcional, RequerimientoDimension.NoAplica, RequerimientoDimension.Opcional,
            Mov(d2: d2, d3: d3), new(d1, d2, d3));
        errores.Should().ContainSingle(e => e.Codigo == "CONTAB_DIM_NO_APLICA" && e.Campo == "dim2Id");
    }

    [Fact]
    public void Todos_los_errores_se_reportan_juntos()
    {
        var errores = Evaluar(RequerimientoDimension.NoAplica, RequerimientoDimension.Obligatorio, RequerimientoDimension.Obligatorio,
            Mov(d1: Guid.NewGuid()), new(Guid.NewGuid(), null, null));
        errores.Select(e => e.Campo).Should().BeEquivalentTo(["dim1Id", "dim2Id", "dim3Id"]);
    }

    [Fact]
    public void Mensaje_de_regla_heredada_indica_la_cuenta_que_la_define()
    {
        var rs = ValidadorDimensiones.Resolver(Cadena, [R(Raiz, DimensionContable.Dim2, RequerimientoDimension.Obligatorio)], null, Dia, Opc());
        ValidadorDimensiones.EvaluarRequerimientos(rs, Mov(), new(null, null, null), "la cuenta FIX-501.01.01")
            .Single().Mensaje.Should().Contain("regla definida en la cuenta FIX-501");
    }

    // ── Opciones ─────────────────────────────────────────────────────────────

    [Fact]
    public void Hoy_se_calcula_en_la_zona_configurada()
    {
        var o = Opc();
        // 2026-10-05 02:00 UTC = 2026-10-04 20:00 en Mérida (UTC-6).
        o.Hoy(new DateTimeOffset(2026, 10, 5, 2, 0, 0, TimeSpan.Zero)).Should().Be(new DateOnly(2026, 10, 4));
    }

    [Fact]
    public void Opciones_invalidas_se_reportan()
    {
        var o = new DimensionesOpciones { ZonaHoraria = "Marte/Olimpo", SinReglaEs = (RequerimientoDimension)7 };
        o.Validar().Should().HaveCount(2);
    }
}
