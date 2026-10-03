using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// Ronda «reglas de Laura» (P19–P26): afectabilidad derivada, conversión dinámica, títulos de reporte, Reporte, rubros,
/// colectivas ampliadas y padre explícito. Datos FIX-*, ficticios.
/// </summary>
public class ReglasLauraTests
{
    private static ResultadoAnalisis Laura(ExistenteCatalogo? ex = null, CatalogoOpciones? o = null) =>
        new ImportadorCatalogo(new FormatoCatalogo(o ?? CatalogoOpciones.Predeterminadas()))
            .Analizar(LectorTabla.Leer(FormatoLaura()), null, ex ?? ExistenteCatalogo.Vacio);

    private static FilaAnalizada F(ResultadoAnalisis r, string codigo) => r.Filas.Single(x => x.Codigo == codigo);

    // ── P19: afectabilidad derivada ──────────────────────────────────────────

    [Theory]
    [InlineData(1, false, TipoCuenta.Titulo)]
    [InlineData(1, true, TipoCuenta.Titulo)]
    [InlineData(2, true, TipoCuenta.Titulo)]
    [InlineData(2, false, TipoCuenta.Afectable)]
    [InlineData(3, true, TipoCuenta.Titulo)]
    [InlineData(4, false, TipoCuenta.Afectable)]
    public void Nivel_1_o_con_hijas_acumula_y_nivel_2_o_mas_sin_hijas_es_afectable(int nivel, bool hijas, TipoCuenta esperado) =>
        CuentaContable.DerivarTipo(nivel, hijas).Should().Be(esperado);

    [Fact]
    public void Formato_Laura_se_importa_completo_con_titulos_omitidos_y_tipo_derivado()
    {
        var r = Laura();
        r.PuedeAplicar.Should().BeTrue(string.Join(" | ", r.Hallazgos.Where(h => h.Severidad == "Error").Select(h => $"{h.Fila}:{h.Codigo}")));
        r.Vacias.Should().Be(2);
        r.FilasTitulo.Should().Be(3);
        r.Filas.Count(x => x.Accion == Accion.Omitir).Should().Be(3);
        r.Filas.Count(x => x.Accion == Accion.Crear).Should().Be(23);

        F(r, "FIX-101.00.00.00").Tipo.Should().Be(TipoCuenta.Titulo);       // nivel 1
        F(r, "FIX-101.01.00.00").Tipo.Should().Be(TipoCuenta.Titulo);       // nivel 2 con hijas
        F(r, "FIX-101.01.01.00").Tipo.Should().Be(TipoCuenta.Afectable);    // nivel 3 sin hijas
        F(r, "FIX-101.02.00.00").Tipo.Should().Be(TipoCuenta.Afectable);    // nivel 2 sin hijas
        // Mismo nombre en nivel 2 (acumula) y nivel 3 (afectable): no es duplicado.
        F(r, "FIX-601.01.00.00").Tipo.Should().Be(TipoCuenta.Titulo);
        F(r, "FIX-601.01.01.00").Tipo.Should().Be(TipoCuenta.Afectable);
        F(r, "FIX-601.01.01.00").Nombre.Should().Be(F(r, "FIX-601.01.00.00").Nombre);
        // Ambas naturalezas y la vacía (pendiente) se conservan.
        F(r, "FIX-201.01.00.00").Naturaleza.Should().Be(NaturalezaCuenta.Acreedora);
        F(r, "FIX-101.02.00.00").Naturaleza.Should().Be(NaturalezaCuenta.Deudora);
        F(r, "FIX-701.01.00.00").Naturaleza.Should().BeNull();
        F(r, "FIX-701.01.00.00").Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CAMPO_PENDIENTE");
        // P22: «Reporte» se conserva tal cual en grupo de reporte.
        F(r, "FIX-601.01.01.00").Grupo.Should().Be("Estado de Pérdidas y Ganancias (Resultado)");
        r.ColumnasEncontradas.Should().Contain(["grupo_reporte", "naturaleza", "codigo_padre", "nivel_contable", "codigo_agrupador"]);
    }

    [Fact]
    public void Fila_de_titulo_sin_codigo_se_omite_con_aviso_y_una_fila_sin_codigo_que_no_es_titulo_es_error()
    {
        var r = Laura();
        var titulo = r.Filas.First(x => x.EsTituloReporte);
        titulo.Accion.Should().Be(Accion.Omitir);
        titulo.Errores.Should().ContainSingle().Which.Should().Match<ErrorFila>(e =>
            e.Codigo == "CONTAB_IMPORT_FILA_TITULO" && e.Severidad == "Advertencia"
            && e.Mensaje == "Fila de título de reporte (sin código): no es una cuenta y no se carga.");

        var sinCodigo = Analizar("codigo;nombre;Tipo\n;FIX sin código;Activo circulante\nFIX-1;FIX Uno;Activo\n");
        sinCodigo.PuedeAplicar.Should().BeFalse();
        sinCodigo.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_CODIGO_FORMATO" && e.Severidad == "Error");
    }

    [Fact]
    public void Alias_de_titulo_es_configurable()
    {
        var o = CatalogoOpciones.Predeterminadas();
        o.Importacion.TiposTitulo = ["encabezado"];
        var r = Analizar("codigo;nombre;Tipo\n;FIX Encabezado;Encabezado\n;FIX Título;Título\nFIX-1;FIX Uno;x\n", o);
        r.Filas[0].Accion.Should().Be(Accion.Omitir);
        r.Filas[1].Accion.Should().Be(Accion.Rechazar, "«Título» ya no es alias con esta configuración");
    }

    // ── P26: padre explícito (caso fila 47) ──────────────────────────────────

    [Fact]
    public void Padre_explicito_respeta_el_nivel_recibido_sin_aviso_de_nivel_ni_huerfana()
    {
        var r = Laura();
        var f = F(r, "FIX-170.05.00.07");
        f.PadreCodigo.Should().Be("FIX-170.00.00.00");
        f.Nivel.Should().Be(2);
        f.Tipo.Should().Be(TipoCuenta.Afectable);
        f.Errores.Should().NotContain(e => e.Codigo == "CONTAB_IMPORT_NIVEL_DISCREPANTE" || e.Codigo == "CONTAB_IMPORT_PADRE_INEXISTENTE");
        r.Huerfanas.Should().Be(0);
        F(r, "FIX-170.01.00.001").Codigo.Should().HaveLength(17, "el código se conserva completo (sin homologar)");
        F(r, "FIX-170.01.00.001").Nivel.Should().Be(3);
    }

    // ── P24: rubros ──────────────────────────────────────────────────────────

    [Fact]
    public void Rubros_quedan_fuera_del_arbol_sin_exigencias_de_afectable_y_agrupan_por_orden_las_cuentas_de_nivel_1()
    {
        var r = Laura();
        var rubros = r.Filas.Where(x => x.Clase == ClaseCuenta.Rubro).Select(x => x.Codigo).ToList();
        rubros.Should().Equal("FIX-100.00.00.00", "FIX-600.00.00.00", "FIX-700.00.00.00"); // «Acumula rubro» = rubro
        foreach (var c in rubros)
        {
            F(r, c!).Tipo.Should().Be(TipoCuenta.Titulo);
            F(r, c!).PadreCodigo.Should().BeNull();
            F(r, c!).Errores.Should().BeEmpty("un rubro no recibe avisos de naturaleza ni de tipo");
        }
        F(r, "FIX-101.00.00.00").RubroCodigo.Should().Be("FIX-100.00.00.00");
        F(r, "FIX-170.00.00.00").RubroCodigo.Should().Be("FIX-100.00.00.00");
        F(r, "FIX-201.00.00.00").RubroCodigo.Should().Be("FIX-100.00.00.00");
        F(r, "FIX-601.00.00.00").RubroCodigo.Should().Be("FIX-600.00.00.00");
        F(r, "FIX-701.00.00.00").RubroCodigo.Should().Be("FIX-700.00.00.00");
        F(r, "FIX-101.01.00.00").RubroCodigo.Should().BeNull("solo las cuentas de nivel 1 pertenecen a un rubro");

        var o = CatalogoOpciones.Predeterminadas();
        o.Importacion.RubroPorOrden = false;
        Laura(o: o).Filas.Should().OnlyContain(x => x.RubroCodigo == null);
    }

    [Fact]
    public void Un_rubro_no_puede_ser_padre_ni_tener_padre_explicito()
    {
        var hijo = Analizar("codigo;nombre;Tipo;codigo_padre\nFIX-1.00;FIX Rubro;Rubro;\nFIX-1.10;FIX Hija;Activo;\n");
        hijo.Filas[1].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_PADRE_INVALIDO");
        var conPadre = Analizar("codigo;nombre;Tipo;codigo_padre\nFIX-2;FIX Raíz;Activo;\nFIX-3;FIX Rubro;Rubro;FIX-2\n");
        conPadre.Filas[1].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_RUBRO_INVALIDO");
    }

    [Fact]
    public void Perfilado_cuenta_titulos_rubros_y_pendientes_solo_de_cuentas()
    {
        var p = System.Text.Json.JsonSerializer.SerializeToElement(new Perfilador(new FormatoCatalogo(CatalogoOpciones.Predeterminadas())).Perfilar(Laura()));
        p.GetProperty("Resumen").GetProperty("FilasTitulo").GetInt32().Should().Be(3);
        p.GetProperty("Resumen").GetProperty("Rubros").GetInt32().Should().Be(3);
        p.GetProperty("PendientesValidacion").GetProperty("SinNaturaleza").GetInt32().Should().Be(2, "solo las 2 cuentas de orden; los rubros no cuentan");
        p.GetProperty("Estructura").GetProperty("Afectables").GetInt32().Should().BeGreaterThan(0);
    }

    // ── P20: conversión dinámica en importación ──────────────────────────────

    private static ExistenteCatalogo ConHojaNivel2(bool usada, CuentaControl control = CuentaControl.Ninguna) => new(
    [
        new(Guid.NewGuid(), "FIX-5.00", "FIX Raíz", null, NaturalezaCuenta.Deudora, TipoCuenta.Titulo, CuentaControl.Ninguna, null, null, true, false),
        new(Guid.NewGuid(), "FIX-5.10", "FIX Hoja", "FIX-5.00", NaturalezaCuenta.Deudora, TipoCuenta.Afectable, control, null, null, true, usada),
    ], new Dictionary<(string, string), string>());

    [Fact]
    public void Hija_bajo_afectable_existente_sin_uso_la_convierte_y_con_uso_se_rechaza()
    {
        const string csv = "codigo;nombre;codigo_padre;naturaleza\nFIX-5.10.01;FIX Nueva;FIX-5.10;Deudora\n";
        var ok = Analizar(csv, ex: ConHojaNivel2(usada: false));
        ok.PuedeAplicar.Should().BeTrue();
        ok.PadresAConvertir.Should().Equal("FIX-5.10");
        ok.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_IMPORT_PADRE_CONVERTIDO" && e.Severidad == "Advertencia");

        var usada = Analizar(csv, ex: ConHojaNivel2(usada: true));
        usada.PuedeAplicar.Should().BeFalse();
        usada.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO");

        var colectiva = Analizar(csv, ex: ConHojaNivel2(usada: false, CuentaControl.Deudores));
        colectiva.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE");
    }

    // ── P23: colectivas ampliadas ────────────────────────────────────────────

    [Theory]
    [InlineData("Deudores", CuentaControl.Deudores)]
    [InlineData("deudor", CuentaControl.Deudores)]
    [InlineData("ACREEDORES", CuentaControl.Acreedores)]
    [InlineData("acreedor", CuentaControl.Acreedores)]
    [InlineData("cliente", CuentaControl.Clientes)]
    [InlineData("Proveedores", CuentaControl.Proveedores)]
    public void Alias_de_cuenta_colectiva(string texto, CuentaControl esperado) =>
        FormatoCatalogo.ParseControl(texto).Should().Be(esperado);

    [Fact]
    public void Origenes_por_defecto_y_manual_nunca_admitido_en_configuracion()
    {
        var o = CatalogoOpciones.Predeterminadas();
        o.OrigenesControl["Deudores"].Should().Equal("AuxiliarCxC");
        o.OrigenesControl["Acreedores"].Should().Equal("AuxiliarCxP");
        o.OrigenesControl["Deudores"] = ["Manual"];
        o.Validar().Should().Contain(x => x.Contains("no puede admitir Manual"));
    }

    [Fact]
    public void Colectiva_importada_en_nivel_2_es_valida_y_en_nivel_1_es_error()
    {
        var ok = Analizar("codigo;nombre;cuenta_control\nFIX-1.00;FIX R;\nFIX-1.10;FIX Deudores;Deudores\n");
        ok.PuedeAplicar.Should().BeTrue();
        ok.Filas[1].Control.Should().Be(CuentaControl.Deudores);
        var raiz = Analizar("codigo;nombre;cuenta_control\nFIX-2.00;FIX Acreedores;Acreedores\n");
        raiz.Filas[0].Errores.Should().Contain(e => e.Codigo == "CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE");
    }

    // ── Dominio ──────────────────────────────────────────────────────────────

    [Fact]
    public void Rubro_no_tiene_padre_ni_es_colectivo_nunca_esta_pendiente_y_solo_las_raices_pertenecen_a_un_rubro()
    {
        var rubro = new CuentaContable(Guid.NewGuid(), "FIX-R", "FIX Rubro", null, 1, null, TipoCuenta.Afectable, CuentaControl.Ninguna, null, null, ClaseCuenta.Rubro);
        rubro.Tipo.Should().Be(TipoCuenta.Titulo, "un rubro nunca recibe movimientos");
        rubro.PendienteValidacion.Should().BeFalse();
        ((Action)(() => rubro.Editar("x", Guid.NewGuid(), 2, null, null, CuentaControl.Ninguna, null, null)))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_CUENTA_RUBRO_INVALIDO");
        ((Action)(() => rubro.AsignarRubro(Guid.NewGuid()))).Should().Throw<BusinessRuleException>();

        var raiz = new CuentaContable(Guid.NewGuid(), "FIX-1", "FIX", null, 1, null, TipoCuenta.Titulo, CuentaControl.Ninguna, null, null);
        raiz.AsignarRubro(rubro.Id);
        raiz.RubroId.Should().Be(rubro.Id);
        raiz.Editar("FIX", Guid.NewGuid(), 2, null, TipoCuenta.Afectable, CuentaControl.Ninguna, null, null);
        raiz.RubroId.Should().BeNull("al pasar a tener padre deja de pertenecer al rubro");
    }
}
