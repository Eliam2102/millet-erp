using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.UnitTests.Fixtures;
using static Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// Los MISMOS casos contra 3 formatos de código ficticios (§20.5): demuestra que cambiar de formato es
/// solo configuración. Datos FIX-*, no reales.
/// </summary>
public class ImportadorFormatosTests
{
    public static IEnumerable<object[]> Todos => FormatosTeoria();
    public static IEnumerable<object[]> ConRelleno => FormatosTeoria().Skip(1); // B y C (A acepta el código sin ceros por patrón permisivo)

    private static ResultadoAnalisis Run(FormatoPrueba f, string csv, ExistenteCatalogo? ex = null) =>
        Analizar(csv, f.Config(), ex);

    private static string[] Codigos(ResultadoAnalisis r, int fila) =>
        [.. r.Filas.First(x => x.Fila == fila).Errores.Select(e => e.Codigo)];

    [Theory, MemberData(nameof(Todos))]
    public void Feliz_crea_1_titulo_raiz_1_titulo_y_2_afectables_con_niveles_1_2_3_3(FormatoPrueba f)
    {
        var r = Run(f, f.CsvFeliz());
        r.PuedeAplicar.Should().BeTrue();
        r.Filas.Select(x => x.Accion).Should().OnlyContain(a => a == Accion.Crear);
        r.Filas.Select(x => x.Nivel).Should().Equal(1, 2, 3, 3);
        r.Filas.Select(x => x.Codigo).Should().Equal(f.Raiz, f.Titulo, f.Hoja1, f.Hoja2);
        r.Huella.Should().HaveLength(64);
    }

    [Theory, MemberData(nameof(Todos))]
    public void Reimportar_el_mismo_archivo_da_SinCambios_en_todas_las_filas(FormatoPrueba f)
    {
        var primera = Run(f, f.CsvFeliz());
        var existentes = primera.Filas.Select(x => new CuentaExistente(Guid.NewGuid(), x.Codigo!, x.Nombre!, x.PadreCodigo,
            x.Naturaleza, x.Tipo, x.Control, x.Agrupador, x.Grupo, true, false)).ToList();
        var segunda = Run(f, f.CsvFeliz(), new ExistenteCatalogo(existentes, new Dictionary<(string, string), string>()));
        segunda.Filas.Select(x => x.Accion).Should().OnlyContain(a => a == Accion.SinCambios);
        segunda.Huella.Should().Be(primera.Huella);
    }

    [Theory, MemberData(nameof(Todos))]
    public void Codigo_duplicado_en_archivo_es_error_en_la_segunda_fila(FormatoPrueba f)
    {
        var r = Run(f, f.Csv((f.Raiz, "R", f.Pad(null), "Deudora", "Titulo"), (f.Raiz, "R otra vez", f.Pad(null), "Deudora", "Titulo")));
        Codigos(r, 3).Should().Contain("CONTAB_IMPORT_CODIGO_DUPLICADO_EN_ARCHIVO");
        r.PuedeAplicar.Should().BeFalse();
        r.DuplicadosCodigo.Should().Be(1);
    }

    [Theory, MemberData(nameof(Todos))]
    public void Huerfana_si_el_padre_no_esta_en_archivo_ni_en_catalogo(FormatoPrueba f)
    {
        var r = Run(f, f.Csv((f.Hoja1, "Hoja", f.Pad(f.Titulo), "Deudora", "Afectable")));
        Codigos(r, 2).Should().Contain("CONTAB_IMPORT_PADRE_INEXISTENTE");
        r.Huerfanas.Should().Be(1);
    }

    [Theory, MemberData(nameof(Todos))]
    public void Ciclo_en_el_archivo_rechaza_ambas_filas(FormatoPrueba f)
    {
        // Padre explícito (siempre se acepta, también en PorSegmentos): hoja1 → hoja2 → hoja1.
        var r = Run(f, f.Csv((f.Hoja1, "H1", f.Hoja2, "Deudora", "Titulo"), (f.Hoja2, "H2", f.Hoja1, "Deudora", "Titulo")));
        Codigos(r, 2).Should().Contain("CONTAB_IMPORT_CICLO");
        Codigos(r, 3).Should().Contain("CONTAB_IMPORT_CICLO");
        r.Ciclos.Should().HaveCount(1);
        r.PuedeAplicar.Should().BeFalse();
    }

    [Theory, MemberData(nameof(Todos))]
    public void Codigo_fuera_de_patron_es_error_de_formato(FormatoPrueba f)
    {
        var r = Run(f, f.Csv((f.FueraDePatron, "Mal", null, "Deudora", "Titulo")));
        Codigos(r, 2).Should().Contain("CONTAB_IMPORT_CODIGO_FORMATO");
    }

    [Theory, MemberData(nameof(Todos))]
    public void Padre_despues_del_hijo_se_resuelve(FormatoPrueba f)
    {
        var r = Run(f, f.Csv(
            (f.Hoja1, "H1", f.Pad(f.Titulo), "Deudora", "Afectable"),
            (f.Titulo, "T", f.Pad(f.Raiz), "Deudora", "Titulo"),
            (f.Raiz, "R", f.Pad(null), "Deudora", "Titulo")));
        r.PuedeAplicar.Should().BeTrue();
        r.Filas.Select(x => x.Nivel).Should().Equal(3, 2, 1);
    }

    [Theory, MemberData(nameof(Todos))]
    public void Padre_afectable_es_error(FormatoPrueba f)
    {
        var r = Run(f, f.Csv((f.Titulo, "T", f.Pad(null), "Deudora", "Afectable"), (f.Hoja1, "H", f.Pad(f.Titulo), "Deudora", "Afectable")));
        Codigos(r, 3).Should().Contain("CONTAB_CUENTA_PADRE_NO_ES_TITULO");
    }

    [Theory, MemberData(nameof(Todos))]
    public void Naturaleza_desconocida_es_error_y_vacia_es_advertencia_pendiente(FormatoPrueba f)
    {
        var mal = Run(f, f.Csv((f.Raiz, "R", f.Pad(null), "XYZ", "Titulo")));
        Codigos(mal, 2).Should().Contain("CONTAB_IMPORT_NATURALEZA_DESCONOCIDA");

        var vacia = Run(f, f.Csv((f.Raiz, "R", f.Pad(null), "", ""), (f.Titulo, "T", f.Pad(f.Raiz), "", "")));
        vacia.PuedeAplicar.Should().BeTrue();
        vacia.Filas.Should().OnlyContain(x => x.Accion == Accion.Crear && x.Naturaleza == null && x.Tipo == null);
        vacia.Hallazgos.Should().Contain(h => h.Codigo == "CONTAB_IMPORT_CAMPO_PENDIENTE" && h.Severidad == "Advertencia");
        vacia.Hallazgos.Should().NotContain(h => h.Severidad == "Error");
    }

    [Theory, MemberData(nameof(ConRelleno))]
    public void Ceros_perdidos_se_rellenan_con_advertencia_solo_si_hay_RellenoCeros(FormatoPrueba f)
    {
        var con = Run(f, f.Csv((f.SinCeros, "X", null, "Deudora", "Titulo")));
        con.Filas[0].Codigo.Should().Be(f.ConCeros);
        con.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CODIGO_RELLENADO" && e.Severidad == "Advertencia");

        var cfg = f.Config();
        cfg.Codigo.RellenoCeros = [];
        var sin = Analizar(f.Csv((f.SinCeros, "X", null, "Deudora", "Titulo")), cfg);
        sin.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CODIGO_FORMATO" && e.Severidad == "Error");
        sin.Filas[0].Errores.Should().NotContain(e => e.Codigo == "CONTAB_IMPORT_CODIGO_RELLENADO");
    }

    [Fact]
    public void Segmento_de_ancho_distinto_deduce_el_padre_con_ceros_del_mismo_ancho_y_el_padre_explicito_lo_resuelve()
    {
        // Último segmento de 3 dígitos: el padre por segmentos es "…02.000", que no existe (huérfana).
        var csv = "codigo;nombre\nFIX-100.00.00.00;R\nFIX-100.10.00.00;T\nFIX-100.10.02.00;H\nFIX-100.10.02.005;A\n";
        var r = Analizar(csv);
        r.Filas.Last().PadreCodigo.Should().Be("FIX-100.10.02.000");
        r.Filas.Last().Errores.Select(e => e.Codigo).Should().Contain("CONTAB_IMPORT_PADRE_INEXISTENTE");

        var explicito = Analizar("codigo;nombre;codigo_padre\nFIX-100.00.00.00;R;\nFIX-100.10.00.00;T;\nFIX-100.10.02.00;H;\nFIX-100.10.02.005;A;FIX-100.10.02.00\n");
        explicito.PuedeAplicar.Should().BeTrue();
        explicito.Filas.Last().Nivel.Should().Be(4);
    }

    [Fact]
    public void Nivel_excedido_segun_configuracion()
    {
        var cfg = CatalogoOpciones.Predeterminadas();
        cfg.NivelMaximo = 2;
        var f = Formatos.First();
        var r = Analizar(f.CsvFeliz(), cfg);
        r.Filas.First(x => x.Codigo == f.Hoja1).Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_NIVEL_EXCEDIDO");
    }
}
