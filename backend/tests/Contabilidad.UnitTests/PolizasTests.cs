using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// F1-CON-04: póliza y partidas (C1.3), numeración por tipo (K10.9) y anulación en negativo (K10.3). Datos FIX de prueba.
/// </summary>
public class PolizasTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Analista = Guid.NewGuid(), Supervisor = Guid.NewGuid(), Sucursal = Guid.NewGuid();
    private static readonly Guid Gasto = Guid.NewGuid(), Banco = Guid.NewGuid();
    private const string Motivo = "FIX importe capturado con error";

    private static TipoDocumentoContable Tipo(string clave = "TT", TipoPolizaSat sat = TipoPolizaSat.Diario) => new(Guid.NewGuid(), clave, "Factura de proveedor", false, sat);

    private static DatosPartida Cargo(decimal importe, Guid? cuenta = null) =>
        new(cuenta ?? Gasto, "FIX-5.1", "FIX gasto", importe, 0m, "MXN", 1m, null, Sucursal, null, null, null, null, null, null, null);

    private static DatosPartida Abono(decimal importe, Guid? cuenta = null) =>
        new(cuenta ?? Banco, "FIX-1.1", null, 0m, importe, "MXN", 1m, null, Sucursal, null, null, null, null, null, null, null);

    private static Poliza Borrador(params DatosPartida[] partidas) => Poliza.CrearManual(Guid.NewGuid(), Tipo(), new DateOnly(2026, 10, 15), 2026, 10,
        "FIX provisión de octubre", null, partidas.Length == 0 ? [Cargo(1000m), Abono(1000m)] : partidas, Analista, "analista");

    private static Poliza Autorizada(int numero = 123)
    {
        var p = Borrador();
        p.Autorizar(numero, Supervisor, "supervisor", Ahora);
        return p;
    }

    // ── K10.9: tipos y folio ────────────────────────────────────────────────

    [Theory]
    [InlineData("TT", true)]
    [InlineData("AS", true)]
    [InlineData("T", false)]
    [InlineData("TTT", false)]
    [InlineData("tt", false)]
    [InlineData("T1", false)]
    public void El_tipo_de_operacion_real_lleva_dos_letras_mayusculas(string clave, bool valido)
    {
        var crear = () => new TipoDocumentoContable(Guid.NewGuid(), clave, "FIX tipo", esPrueba: false);
        if (valido) crear.Should().NotThrow();
        else crear.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_TIPO_DOC_CLAVE_INVALIDA");
    }

    [Fact]
    public void El_tipo_de_prueba_conserva_la_clave_libre_y_el_tipo_sat_se_puede_cambiar()
    {
        var t = new TipoDocumentoContable(Guid.NewGuid(), "FIX-ZZ01", "FIX tipo", esPrueba: true);
        t.TipoSat.Should().Be(TipoPolizaSat.Diario);
        t.Editar("FIX pago a proveedor", TipoPolizaSat.Egreso);
        t.TipoSat.Should().Be(TipoPolizaSat.Egreso);
    }

    [Fact]
    public void El_folio_es_el_tipo_y_seis_digitos_y_solo_existe_al_autorizar()
    {
        var p = Borrador();
        p.Folio.Should().BeNull();
        p.Numero.Should().BeNull();
        p.Autorizar(123, Supervisor, "supervisor", Ahora);
        p.Folio.Should().Be("TT-000123");
        Poliza.FormatearFolio("AS", 1).Should().Be("AS-000001");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_000)]
    public void El_consecutivo_fuera_de_seis_digitos_se_rechaza(int numero)
    {
        var p = Borrador();
        p.Invoking(x => x.Autorizar(numero, Supervisor, "supervisor", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_CONSECUTIVO_AGOTADO");
    }

    // ── C1.3: captura, cuadre, autorización, inmutabilidad ─────────────────

    [Fact]
    public void Un_borrador_puede_no_cuadrar_pero_no_se_autoriza_C13a()
    {
        var p = Borrador(Cargo(1000m), Abono(900m));
        p.Cuadra.Should().BeFalse();
        p.Invoking(x => x.Autorizar(1, Supervisor, "supervisor", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_NO_CUADRA");
        p.Estado.Should().Be(EstadoPoliza.Borrador);
    }

    [Fact]
    public void Quien_captura_no_autoriza_su_propia_poliza_C13c()
    {
        var p = Borrador();
        p.Invoking(x => x.Autorizar(1, Analista, "analista", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_AUTORIZA_QUIEN_CAPTURA");
    }

    [Fact]
    public void La_autorizada_no_se_edita_solo_se_anula_C13b()
    {
        var p = Autorizada();
        p.Invoking(x => x.Editar(p.Fecha, 2026, 10, "FIX otro concepto", [Cargo(5m), Abono(5m)]))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_NO_EDITABLE");
    }

    [Fact]
    public void Una_partida_lleva_un_solo_lado_positivo()
    {
        var ambos = Cargo(10m) with { Abono = 10m };
        var negativo = Cargo(-10m);
        var cero = Cargo(0m);
        ((Action)(() => Borrador(ambos, Abono(10m)))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PARTIDA_UN_SOLO_LADO");
        ((Action)(() => Borrador(cero, Abono(10m)))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PARTIDA_UN_SOLO_LADO");
        ((Action)(() => Borrador(negativo, Abono(10m)))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PARTIDA_IMPORTE_NEGATIVO");
    }

    [Fact]
    public void En_moneda_extranjera_el_importe_en_pesos_corresponde_al_original_por_el_tipo_de_cambio()
    {
        var usd = Cargo(1850m) with { Moneda = "USD", TipoCambio = 18.5m, ImporteOriginal = 100m };
        Borrador(usd, Abono(1850m)).Partidas[0].Moneda.Should().Be("USD");
        var mal = usd with { Cargo = 1900m };
        ((Action)(() => Borrador(mal, Abono(1900m)))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_PARTIDA_CONVERSION_INVALIDA");
    }

    [Theory]
    [InlineData(2026, 10, 15, 2026, 10, true)]
    [InlineData(2026, 10, 15, 2026, 11, false)]
    [InlineData(2026, 12, 31, 2026, 13, true)]
    [InlineData(2026, 12, 30, 2026, 13, false)]
    public void La_fecha_corresponde_al_periodo_y_el_13_es_el_31_de_diciembre(int a, int m, int d, int anio, int numero, bool valido)
    {
        var crear = () => Poliza.CrearManual(Guid.NewGuid(), Tipo(), new DateOnly(a, m, d), anio, numero, "FIX ajuste", null,
            [Cargo(1m), Abono(1m)], Analista, "analista");
        if (valido) crear.Should().NotThrow();
        else crear.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_FECHA_FUERA_DE_PERIODO");
    }

    [Fact]
    public void La_automatica_nace_autorizada_numerada_y_debe_cuadrar()
    {
        var doc = new DocumentoOrigenPoliza("cuentas_por_pagar", "factura_proveedor", Guid.NewGuid(), "A-15", "UUID-FIX");
        var p = Poliza.CrearAutomatica(Guid.NewGuid(), Tipo(), new DateOnly(2026, 10, 15), 2026, 10, "FIX factura A-15", doc, "evento:1",
            [Cargo(116m), Abono(116m)], 1, Ahora);
        p.Estado.Should().Be(EstadoPoliza.Autorizada);
        p.Origen.Should().Be(OrigenPoliza.Automatica);
        p.Folio.Should().Be("TT-000001");
        ((Action)(() => Poliza.CrearAutomatica(Guid.NewGuid(), Tipo(), new DateOnly(2026, 10, 15), 2026, 10, "FIX", doc, "evento:2",
            [Cargo(116m), Abono(100m)], 2, Ahora))).Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_NO_CUADRA");
    }

    // ── K10.3: anulación en negativo ───────────────────────────────────────

    [Fact]
    public void La_anulacion_lleva_las_mismas_partidas_en_negativo_mismo_lado_y_queda_ligada_CA105()
    {
        var original = Autorizada(123);
        var tipo = TipoDe(original);
        var anulacion = original.Anular(Guid.NewGuid(), tipo, original.Fecha, 2026, 10, 124, Motivo, Supervisor, "supervisor", Ahora);

        anulacion.Concepto.Should().Be("Anulación de TT-000123");
        anulacion.Folio.Should().Be("TT-000124");
        anulacion.Estado.Should().Be(EstadoPoliza.Autorizada);
        anulacion.PolizaAnuladaId.Should().Be(original.Id);
        anulacion.EsAnulacion.Should().BeTrue();
        anulacion.Partidas.Select(x => (x.CuentaId, x.Cargo, x.Abono)).Should().Equal(
            original.Partidas.Select(x => (x.CuentaId, -x.Cargo, -x.Abono)));
        anulacion.TotalCargos.Should().Be(-1000m);
        anulacion.TotalAbonos.Should().Be(-1000m);

        original.Estado.Should().Be(EstadoPoliza.Anulada);
        original.AnuladaPorPolizaId.Should().Be(anulacion.Id);
        original.MotivoAnulacion.Should().Be(Motivo);
    }

    [Fact]
    public void La_suma_de_la_original_y_su_anulacion_es_cero_por_cuenta_K103a()
    {
        var original = Autorizada();
        var anulacion = original.Anular(Guid.NewGuid(), TipoDe(original), original.Fecha, 2026, 10, 2, Motivo, Supervisor, "supervisor", Ahora);
        var porCuenta = original.Partidas.Concat(anulacion.Partidas).GroupBy(x => x.CuentaId)
            .Select(g => (Cargos: g.Sum(x => x.Cargo), Abonos: g.Sum(x => x.Abono)));
        porCuenta.Should().AllSatisfy(s => { s.Cargos.Should().Be(0m); s.Abonos.Should().Be(0m); });
    }

    [Fact]
    public void Una_poliza_anulada_no_se_anula_otra_vez_K103b()
    {
        var original = Autorizada();
        var anulacion = original.Anular(Guid.NewGuid(), TipoDe(original), original.Fecha, 2026, 10, 2, Motivo, Supervisor, "supervisor", Ahora);
        original.Invoking(x => x.Anular(Guid.NewGuid(), TipoDe(original), original.Fecha, 2026, 10, 3, Motivo, Supervisor, "supervisor", Ahora))
            .Should().Throw<ConflictException>().Which.Code.Should().Be("CONTAB_POLIZA_YA_ANULADA");
        anulacion.Invoking(x => x.ExigirAnulable(Motivo))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_ANULACION_NO_ANULABLE");
    }

    [Fact]
    public void Solo_se_anula_una_autorizada_y_con_motivo()
    {
        Borrador().Invoking(x => x.ExigirAnulable(Motivo))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_NO_AUTORIZADA");
        Autorizada().Invoking(x => x.ExigirAnulable("corto"))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_POLIZA_MOTIVO_INVALIDO");
    }

    [Fact]
    public void La_anulacion_procede_aunque_el_tipo_ya_este_inactivo()
    {
        var original = Autorizada();
        var tipo = TipoDe(original);
        tipo.CambiarEstatus(activo: false);
        original.Invoking(x => x.Anular(Guid.NewGuid(), tipo, original.Fecha, 2026, 10, 2, Motivo, Supervisor, "supervisor", Ahora))
            .Should().NotThrow();
    }

    [Fact]
    public void La_anulacion_en_otro_periodo_cae_hoy_si_hoy_esta_en_el_o_en_su_primer_dia()
    {
        RegistroPolizas.FechaEnPeriodo(2026, 11, new DateOnly(2026, 11, 20)).Should().Be(new DateOnly(2026, 11, 20));
        RegistroPolizas.FechaEnPeriodo(2026, 11, new DateOnly(2026, 12, 3)).Should().Be(new DateOnly(2026, 11, 1));
        RegistroPolizas.FechaEnPeriodo(2026, 13, new DateOnly(2027, 1, 10)).Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public void El_validador_rechaza_importes_negativos_y_partidas_de_dos_lados()
    {
        var v = new PartidaRequestValidator();
        v.Validate(new PartidaRequest(Gasto, null, 10m, 0m, Sucursal)).IsValid.Should().BeTrue();
        v.Validate(new PartidaRequest(Gasto, null, -10m, 0m, Sucursal)).IsValid.Should().BeFalse();
        v.Validate(new PartidaRequest(Gasto, null, 10m, 10m, Sucursal)).IsValid.Should().BeFalse();
    }

    /// <summary>Tipo con el mismo id que la póliza (la anulación exige el mismo tipo).</summary>
    private static TipoDocumentoContable TipoDe(Poliza p)
    {
        var t = Tipo(p.TipoClave);
        typeof(Millet.SharedKernel.Domain.BaseEntity).GetProperty(nameof(t.Id))!.SetValue(t, p.TipoOperacionId);
        return t;
    }
}
