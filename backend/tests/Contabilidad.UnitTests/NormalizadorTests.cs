using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Contabilidad.UnitTests;

/// <summary>Normalizador único del servidor (§20.2): codificación, delimitador, espacios, alias. Datos FIX-*.</summary>
public class NormalizadorTests
{
    private const string Base = "codigo;nombre;naturaleza;tipo_cuenta\nFIX-1;FIX Uno;Deudora;Afectable\n";

    private static ResultadoAnalisis Run(byte[] b) => Analizar(b);

    [Fact]
    public void Bom_utf8_utf16_y_sin_bom_producen_la_misma_huella()
    {
        var a = Run(Utf8(Base));
        Run(Utf8ConBom(Base)).Huella.Should().Be(a.Huella);
        Run(Utf16ConBom(Base)).Huella.Should().Be(a.Huella);
        Run(Utf8ConBom(Base)).Filas[0].Codigo.Should().Be("FIX-1");
    }

    [Fact]
    public void Windows1252_se_lee_con_advertencia_de_codificacion()
    {
        var r = Run(Windows1252("codigo;nombre;naturaleza;tipo_cuenta\nFIX-1;Cuenta ñandú;Deudora;Afectable\n"));
        r.CodificacionFallback.Should().BeTrue();
        r.Archivo.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CODIFICACION" && e.Severidad == "Advertencia");
        r.Filas[0].Nombre.Should().Be("Cuenta ñandú");
    }

    [Fact]
    public void Caracteres_de_reemplazo_en_utf8_valido_dan_advertencia()
    {
        var r = Run(Utf8("codigo;nombre;naturaleza;tipo_cuenta\nFIX-1;Cuenta �;Deudora;Afectable\n"));
        r.CaracteresReemplazo.Should().Be(1);
        r.Archivo.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CODIFICACION");
    }

    [Theory]
    [InlineData(";")]
    [InlineData(",")]
    [InlineData("\t")]
    public void Delimitador_autodetectado_y_crlf(string d)
    {
        var csv = Base.Replace(";", d).Replace("\n", "\r\n");
        Run(Utf8(csv)).Filas[0].Codigo.Should().Be("FIX-1");
    }

    [Fact]
    public void Comillas_con_delimitador_y_comillas_dobles_escapadas()
    {
        var r = Run(Utf8("codigo,nombre,naturaleza,tipo_cuenta\nFIX-1,\"Uno, con \"\"comillas\"\"\",Deudora,Afectable\n"));
        r.Filas[0].Nombre.Should().Be("Uno, con \"comillas\"");
    }

    [Fact]
    public void Espacios_nbsp_y_mayusculas_se_normalizan()
    {
        var r = Run(Utf8("codigo;nombre;naturaleza;tipo_cuenta\n  fix-1  ;  FIX   Uno  ;  deudora ;AFECTABLE\n"));
        var f = r.Filas[0];
        (f.Codigo, f.Nombre).Should().Be(("FIX-1", "FIX Uno"));
        f.Naturaleza.Should().Be(Domain.NaturalezaCuenta.Deudora);
        f.Tipo.Should().Be(Domain.TipoCuenta.Afectable);
    }

    [Fact]
    public void Filas_vacias_o_de_solo_separadores_se_ignoran_y_se_cuentan()
    {
        var r = Run(Utf8(Sucio_Vacias()));
        r.Filas.Should().HaveCount(1);
        r.Vacias.Should().Be(3);
        r.Filas[0].Fila.Should().Be(4); // numeración del archivo (cabecera = 1)
    }

    [Fact]
    public void Alias_de_cabeceras_sin_acentos_ni_mayusculas()
    {
        var r = Run(Utf8("CÓDIGO;Descripción;Naturaleza;TIPO_CUENTA\nFIX-1;Uno;d;t\n"));
        r.Filas[0].Nombre.Should().Be("Uno");
        r.Filas[0].Tipo.Should().Be(Domain.TipoCuenta.Titulo);
    }

    [Fact]
    public void Columna_obligatoria_ausente_es_error_con_cabeceras_y_alias()
    {
        var act = () => Run(Utf8("codigo;naturaleza\nFIX-1;Deudora\n"));
        var e = act.Should().Throw<BusinessRuleException>().Which;
        e.Code.Should().Be("CONTAB_IMPORT_COLUMNA_FALTANTE");
        e.Message.Should().Contain("nombre").And.Contain("cuenta").And.Contain("codigo");
    }

    [Fact]
    public void Cabecera_desconocida_y_columnas_sin_mapeo_son_advertencias()
    {
        var r = Run(Utf8("codigo;nombre;Tipo;saldo\nFIX-1;Uno;FIX-CAT;100.50\n"));
        r.PuedeAplicar.Should().BeTrue();
        r.Archivo.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_COLUMNA_SIN_MAPEO" && e.Columna == "tipo");
        r.Archivo.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_COLUMNA_IGNORADA" && e.Columna == "saldo");
        r.Filas[0].Tipo.Should().BeNull("la columna Tipo es una categoría informativa, no el título/afectable");
    }

    [Fact]
    public void Archivo_sin_filas_o_invalido_es_error_de_formato()
    {
        var vacio = () => Run(Utf8("codigo;nombre\n"));
        vacio.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_IMPORT_ARCHIVO_VACIO");
        var invalido = () => LectorTabla.Leer(new ImportacionRequest(null, null, "no es base64!!", null, null, null));
        invalido.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_IMPORT_ARCHIVO_INVALIDO");
    }

    [Fact]
    public void Tabla_en_texto_crudo_del_cliente_xlsx_equivale_al_csv()
    {
        var tabla = LectorTabla.Leer(new ImportacionRequest(null, null, null, ["codigo", "nombre", "naturaleza", "tipo_cuenta"],
            [["FIX-1", "FIX Uno", "Deudora", "Afectable"]], null));
        var f = new FormatoCatalogo(CatalogoOpciones.Predeterminadas());
        new ImportadorCatalogo(f).Analizar(tabla, null, ExistenteCatalogo.Vacio).Huella.Should().Be(Run(Utf8(Base)).Huella);
    }

    [Fact]
    public void Limite_de_5000_filas_acepta_5000_y_rechaza_5001_y_el_tiempo_cabe_en_presupuesto()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ok = Run(Utf8(Volumen(5000)));
        sw.Stop();
        ok.PuedeAplicar.Should().BeTrue();
        ok.Filas.Should().HaveCount(5000);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "presupuesto documentado: análisis de 5 000 filas < 10 s");

        var act = () => Run(Utf8(Volumen(5001)));
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_IMPORT_LIMITE_FILAS");
    }
}
