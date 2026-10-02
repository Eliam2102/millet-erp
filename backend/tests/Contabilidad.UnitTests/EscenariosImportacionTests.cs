using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Domain;
using static Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Contabilidad.UnitTests;

/// <summary>Hoja base provisional (P14–P16), cuentas de control (P3), uso (R8, P4) y correspondencia de origen. Datos FIX-*.</summary>
public class EscenariosImportacionTests
{
    private static CuentaExistente Existente(string codigo, string? padre = null, bool usada = false, TipoCuenta? tipo = TipoCuenta.Afectable,
        NaturalezaCuenta? nat = NaturalezaCuenta.Deudora, string nombre = "FIX") =>
        new(Guid.NewGuid(), codigo, nombre, padre, nat, tipo, CuentaControl.Ninguna, null, null, true, usada);

    private static ExistenteCatalogo Cat(IEnumerable<CuentaExistente> cuentas, Dictionary<(string, string), string>? origenes = null) =>
        new([.. cuentas], origenes ?? []);

    // ── Hoja base provisional (P14, P15, P16) ────────────────────────────────

    [Fact]
    public void Hoja_base_mapea_alias_deduce_jerarquia_y_deja_naturaleza_y_tipo_pendientes()
    {
        var r = Analizar(HojaBase());
        r.ColumnasEncontradas.Should().Contain(["codigo", "nombre", "codigo_agrupador", "nivel_contable"]);
        r.Filas[0].Codigo.Should().Be("FIX-100.00.00.00");
        r.Filas[0].Nombre.Should().Be("FIX Activo");
        r.Filas[0].Agrupador.Should().Be("100");
        r.Filas.Take(4).Select(x => x.Nivel).Should().Equal(1, 2, 3, 3);
        // naturaleza y tipo NO se suponen ni se derivan de la jerarquía ni de "Tipo"
        r.Filas.Should().OnlyContain(x => x.Naturaleza == null && x.Tipo == null);
        r.Archivo.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CAMPO_PENDIENTE" && e.Columna == "naturaleza");
        r.Archivo.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_COLUMNA_SIN_MAPEO" && e.Columna == "tipo");
        r.Archivo.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_COLUMNA_SIN_MAPEO" && e.Columna == "nivel_de_cuenta_sat");
    }

    [Fact]
    public void Nivel_contable_discrepante_es_advertencia_y_el_ancho_distinto_queda_huerfano()
    {
        var r = Analizar(HojaBase());
        // fila 5 (FIX-100.10.02.00): nivel contable 2 vs derivado 3 → advertencia, no error
        r.Filas[3].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_NIVEL_DISCREPANTE" && e.Severidad == "Advertencia");
        r.NivelContableDiscrepancias.Should().BeGreaterThan(0);
        // fila 6: último segmento de ancho 3 → padre deducido "…02.000" inexistente
        r.Filas[4].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_PADRE_INEXISTENTE");
        r.Huerfanas.Should().Be(1);
    }

    [Fact]
    public void Cuenta_pendiente_no_se_degrada_al_reimportar_ni_se_borra_un_valor_existente_con_celda_vacia()
    {
        var ex = Cat([Existente("FIX-1", tipo: TipoCuenta.Afectable, nat: NaturalezaCuenta.Acreedora)]);
        var r = Analizar("codigo;nombre;naturaleza;tipo_cuenta\nFIX-1;FIX;;\n", ex: ex);
        r.Filas[0].Naturaleza.Should().Be(NaturalezaCuenta.Acreedora);
        r.Filas[0].Tipo.Should().Be(TipoCuenta.Afectable);
        r.Filas[0].Accion.Should().Be(Accion.SinCambios);
    }

    // ── Cuentas de control (P3, §20.1) ───────────────────────────────────────

    private static CatalogoOpciones ConControl(string codigo, string tipo)
    {
        var o = CatalogoOpciones.Predeterminadas();
        o.CuentasControl = [new() { Codigo = codigo, Tipo = tipo }];
        return o;
    }

    [Fact]
    public void Codigo_en_CuentasControl_marca_la_cuenta_aunque_la_columna_no_exista()
    {
        var r = Analizar("codigo;nombre;naturaleza;tipo_cuenta\nFIX-C1;Ctl;Deudora;Afectable\n", ConControl("FIX-C1", "Clientes"));
        r.Filas[0].Control.Should().Be(CuentaControl.Clientes);
        r.PuedeAplicar.Should().BeTrue();
    }

    [Fact]
    public void Columna_cuenta_control_que_contradice_la_configuracion_es_conflicto()
    {
        var r = Analizar("codigo;nombre;naturaleza;tipo_cuenta;cuenta_control\nFIX-C1;Ctl;Deudora;Afectable;Proveedores\n", ConControl("FIX-C1", "Clientes"));
        r.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CONTROL_CONFLICTO");
    }

    [Fact]
    public void Control_en_titulo_o_sin_tipo_explicito_es_error()
    {
        var titulo = Analizar("codigo;nombre;naturaleza;tipo_cuenta\nFIX-C1;Ctl;Deudora;Titulo\n", ConControl("FIX-C1", "Clientes"));
        titulo.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE");
        var sinTipo = Analizar("codigo;nombre\nFIX-C1;Ctl\n", ConControl("FIX-C1", "Clientes"));
        sinTipo.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE");
    }

    // ── Uso, R8 y origen ─────────────────────────────────────────────────────

    [Fact]
    public void Cambiar_padre_naturaleza_o_tipo_de_cuenta_usada_se_rechaza_pero_el_nombre_no()
    {
        var ex = Cat([Existente("FIX-9", tipo: TipoCuenta.Titulo), Existente("FIX-1", padre: null, usada: true)]);
        var bloquea = Analizar("codigo;nombre;codigo_padre;naturaleza;tipo_cuenta\nFIX-1;FIX;;Acreedora;Afectable\n", ex: ex);
        bloquea.Filas[0].Accion.Should().Be(Accion.Rechazar);
        bloquea.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO");

        var permite = Analizar("codigo;nombre;codigo_padre;naturaleza;tipo_cuenta\nFIX-1;FIX nombre nuevo;;Deudora;Afectable\n", ex: ex);
        permite.Filas[0].Accion.Should().Be(Accion.Actualizar);
    }

    [Fact]
    public void Uso_en_un_descendiente_tambien_bloquea_al_ancestro()
    {
        var ex = Cat([Existente("FIX-1", tipo: TipoCuenta.Titulo), Existente("FIX-2", padre: "FIX-1", usada: true)]);
        var r = Analizar("codigo;nombre;codigo_padre;naturaleza;tipo_cuenta\nFIX-1;FIX;;Acreedora;Titulo\n", ex: ex);
        r.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO");
    }

    [Fact]
    public void Origen_resuelve_identidad_y_codigo_distinto_para_el_mismo_origen_es_error()
    {
        var ex = Cat([Existente("FIX-1")], new() { [("FIX-FUENTE", "O-1")] = "FIX-1" });
        var igual = Analizar("fuente;codigo_origen;codigo;nombre;naturaleza;tipo_cuenta\nFIX-FUENTE;O-1;FIX-1;FIX;Deudora;Afectable\n", ex: ex);
        igual.Filas[0].Accion.Should().Be(Accion.SinCambios);

        var otro = Analizar("fuente;codigo_origen;codigo;nombre;naturaleza;tipo_cuenta\nFIX-FUENTE;O-1;FIX-2;FIX;Deudora;Afectable\n", ex: ex);
        otro.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_ORIGEN_CODIGO_DISTINTO");

        var nuevoOrigen = Analizar("fuente;codigo_origen;codigo;nombre;naturaleza;tipo_cuenta\nFIX-FUENTE;O-9;FIX-1;FIX;Deudora;Afectable\n", ex: ex);
        nuevoOrigen.Filas[0].Accion.Should().Be(Accion.Actualizar);
        nuevoOrigen.Filas[0].OrigenNuevo.Should().BeTrue();
    }

    [Fact]
    public void Origen_duplicado_en_archivo_es_error()
    {
        var r = Analizar("fuente;codigo_origen;codigo;nombre\nFIX-F;O-1;FIX-1;A\nFIX-F;O-1;FIX-2;B\n");
        r.Filas[1].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CODIGO_DUPLICADO_EN_ARCHIVO" && e.Columna == "codigo_origen");
        r.DuplicadosOrigen.Should().Be(1);
    }

    [Fact]
    public void Padre_existente_inactivo_es_invalido_y_afectable_con_hijas_es_error()
    {
        var inactivo = Existente("FIX-1", tipo: TipoCuenta.Titulo) with { Activa = false };
        var r = Analizar("codigo;nombre;codigo_padre\nFIX-1.1;Hija;FIX-1\n", ex: Cat([inactivo]));
        r.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_PADRE_INVALIDO");

        var ex = Cat([Existente("FIX-2", tipo: TipoCuenta.Titulo), Existente("FIX-2.1", padre: "FIX-2")]);
        var afectable = Analizar("codigo;nombre;codigo_padre;naturaleza;tipo_cuenta\nFIX-2;T;;Deudora;Afectable\n", ex: ex);
        afectable.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_AFECTABLE_CON_HIJAS");
    }

    [Fact]
    public void Herencia_de_naturaleza_solo_aplica_si_la_configuracion_la_enciende()
    {
        const string csv = "codigo;nombre;naturaleza;tipo_cuenta\nFIX-1.00;T;Deudora;Titulo\nFIX-1.10;H;Acreedora;Afectable\n";
        Analizar(csv).PuedeAplicar.Should().BeTrue();
        var cfg = CatalogoOpciones.Predeterminadas();
        cfg.HerenciaNaturaleza = true;
        Analizar(csv, cfg).Filas[1].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_NATURALEZA_INVALIDA");
    }

    // ── Huella y perfilado ───────────────────────────────────────────────────

    [Fact]
    public void Huella_cambia_si_cambia_el_contenido_y_no_si_cambia_el_formato_del_archivo()
    {
        var a = Analizar("codigo;nombre\nFIX-1;Uno\n").Huella;
        Analizar("codigo,nombre\r\nfix-1,  Uno \r\n").Huella.Should().Be(a);
        Analizar("codigo;nombre\nFIX-1;Dos\n").Huella.Should().NotBe(a);
    }

    private static System.Text.Json.JsonElement Perfil(ResultadoAnalisis r) =>
        System.Text.Json.JsonSerializer.SerializeToElement(new Perfilador(new FormatoCatalogo(CatalogoOpciones.Predeterminadas())).Perfilar(r));

    [Fact]
    public void Perfilado_cuenta_pendientes_discrepancias_sin_mapeo_y_agrupa_por_codigo_de_error()
    {
        var p = Perfil(Analizar(HojaBase()));

        var pend = p.GetProperty("PendientesValidacion");
        pend.GetProperty("SinNaturaleza").GetInt32().Should().Be(5);
        pend.GetProperty("SinTipo").GetInt32().Should().Be(5);
        var nivel = p.GetProperty("NivelContable");
        nivel.GetProperty("Comparadas").GetInt32().Should().Be(4);
        nivel.GetProperty("Discrepancias").GetInt32().Should().Be(1);
        nivel.GetProperty("Ejemplos")[0].GetInt32().Should().Be(5);
        var tipo = p.GetProperty("ColumnasSinMapeo").EnumerateArray().First(c => c.GetProperty("Columna").GetString() == "tipo");
        (tipo.GetProperty("FilasConValor").GetInt32(), tipo.GetProperty("ValoresDistintos").GetInt32()).Should().Be((4, 2));
        var huerfana = p.GetProperty("PorCodigoError").EnumerateArray().First(e => e.GetProperty("Codigo").GetString() == "CONTAB_IMPORT_PADRE_INEXISTENTE");
        huerfana.GetProperty("Conteo").GetInt32().Should().Be(1);
        huerfana.GetProperty("Ejemplos")[0].GetProperty("Fila").GetInt32().Should().Be(6);
        p.GetProperty("QueSeReabre").EnumerateArray().Select(q => q.GetProperty("Decision").GetString())
            .Should().Contain(d => d!.Contains("P14")).And.Contain(d => d!.Contains("P16"));
    }

    [Fact]
    public void Perfilado_no_vuelca_codigos_ni_nombres_de_cuentas()
    {
        var json = Perfil(Analizar(HojaBase())).GetRawText();
        json.Should().NotContain("FIX-100").And.NotContain("FIX Caja").And.NotContain("FIX Activo");
    }
}
