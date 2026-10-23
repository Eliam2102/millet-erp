using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.DiferenciasFiscales;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Application.Workers;
using C = Millet.Integraciones.Aw.Application.DiferenciasFiscales.CamposDiferenciaFiscal;

namespace Millet.Integraciones.Aw.UnitTests.DiferenciasFiscales;

/// <summary>U1.8b: normalización y reglas de comparación (sin E/S). Datos 100 % sintéticos.</summary>
public sealed class ComparadorDiferenciasFiscalesTests
{
    private static readonly Guid Id = Guid.Parse("0199e000-0000-7000-8000-000000000001");

    internal static AwClienteOrigenFila Aw(int id = 1001, string? name1 = "CLIENTE DEMO", string? name2 = null,
        string? ustId = "DEMO010101AB1", string? plz = "97000", string? regimen = "601") => new(
        id, 1, name1, name2, null, null, null, plz, null, "MEX", ustId, null, null, null, null, "CONTADO", "PESOSMX",
        null, null, null, 1, 0, null, null, [], Bonitaet: "MORAL DEMO", RegimenFiscalOrigen: regimen);

    private static ClienteFiscalErp Erp(string razon = "CLIENTE DEMO", string? rfc = "DEMO010101AB1", string? regimen = "601", string? cp = "97000") =>
        new(Id, "1001", razon, rfc, regimen, cp);

    [Theory]
    [InlineData("demo-010101.ab 1", "DEMO010101AB1")]
    [InlineData("  XEXX010101000 ", "XEXX010101000")]
    [InlineData("<indf>", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Rfc_se_normaliza_sin_guiones_puntos_ni_espacios(string? crudo, string? esperado) =>
        ComparadorDiferenciasFiscales.NormalizarRfc(crudo).Should().Be(esperado);

    [Theory]
    [InlineData(" CLIENTE  DEMO ", " SA DE CV", "CLIENTE  DEMO SA DE CV")]
    [InlineData("CLIENTE DEMO", null, "CLIENTE DEMO")]
    [InlineData("CLIENTE DEMO", "", "CLIENTE DEMO")]
    [InlineData("<indf>", null, null)]
    public void Razon_social_de_A_W_es_NAME1_mas_NAME2(string? name1, string? name2, string? esperado) =>
        ComparadorDiferenciasFiscales.RazonSocialAw(name1, name2).Should().Be(esperado);

    [Fact]
    public void Razon_social_ignora_mayusculas_acentos_espacios_puntos_y_comas_pero_no_el_regimen_societario()
    {
        ComparadorDiferenciasFiscales.ClaveRazonSocial("Peña  Demo, S.A.")
            .Should().Be(ComparadorDiferenciasFiscales.ClaveRazonSocial("PENA DEMO SA"));
        ComparadorDiferenciasFiscales.ClaveRazonSocial("PENA DEMO SA DE CV")
            .Should().NotBe(ComparadorDiferenciasFiscales.ClaveRazonSocial("PENA DEMO"));
    }

    [Fact]
    public void Cliente_igual_no_tiene_diferencias_aunque_cambien_formato_y_mayusculas()
    {
        var aw = Aw(name1: "Cliente  Demo", ustId: "demo-010101-ab1", plz: " 97000 ");
        ComparadorDiferenciasFiscales.CompararCliente(Erp(), aw).Should().BeEmpty();
    }

    [Fact]
    public void CA_a_regimen_601_en_A_W_y_612_en_ERP_aparece_con_ambos_valores()
    {
        var d = ComparadorDiferenciasFiscales.CompararCliente(Erp(regimen: "612"), Aw(regimen: "601")).Should().ContainSingle().Subject;
        d.Campo.Should().Be(C.RegimenFiscal);
        (d.ValorOrigen, d.ValorErp, d.Estado, d.Fuente).Should().Be(("601", "612", EstadoDiferenciaFiscal.Diferente, "A+W"));
        d.Huella.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Cliente_reporta_faltante_en_ERP_y_sin_dato_en_A_W()
    {
        var difs = ComparadorDiferenciasFiscales.CompararCliente(Erp(regimen: null, cp: "97000"), Aw(regimen: "601", plz: "<indf>"));
        difs.Should().HaveCount(2);
        difs.Single(d => d.Campo == C.RegimenFiscal).Estado.Should().Be(EstadoDiferenciaFiscal.Faltante);
        difs.Single(d => d.Campo == C.CodigoPostalFiscal).Estado.Should().Be(EstadoDiferenciaFiscal.SinDatoEnOrigen);
    }

    [Fact]
    public void Cliente_compara_razon_social_NAME1_NAME2_rfc_y_cp()
    {
        var difs = ComparadorDiferenciasFiscales.CompararCliente(
            Erp(razon: "CLIENTE DEMO", rfc: "DEMO010101AB2", cp: "97001"),
            Aw(name1: "CLIENTE DEMO", name2: "SA DE CV"));
        difs.Select(d => d.Campo).Should().BeEquivalentTo([C.RazonSocial, C.Rfc, C.CodigoPostalFiscal]);
        difs.Single(d => d.Campo == C.RazonSocial).ValorOrigen.Should().Be("CLIENTE DEMO SA DE CV");
    }

    [Fact]
    public void La_huella_cambia_con_el_valor_y_no_con_el_formato()
    {
        var h1 = ComparadorDiferenciasFiscales.CompararCliente(Erp(regimen: "612"), Aw(regimen: "601")).Single().Huella;
        var h2 = ComparadorDiferenciasFiscales.CompararCliente(Erp(regimen: "612"), Aw(regimen: " 601 ")).Single().Huella;
        var h3 = ComparadorDiferenciasFiscales.CompararCliente(Erp(regimen: "626"), Aw(regimen: "601")).Single().Huella;
        h2.Should().Be(h1);
        h3.Should().NotBe(h1);
        ComparadorDiferenciasFiscales.Huella(TipoEntidadDiferenciaFiscal.Cliente, C.Rfc, null, "")
            .Should().NotBe(ComparadorDiferenciasFiscales.Huella(TipoEntidadDiferenciaFiscal.Cliente, C.Rfc, "", null));
    }

    // ---------- productos ----------

    private static EsperadoFiscalProducto Esperado(string? prodServ = "30171706", string? unidad = "MTK", string? obj = "02") =>
        new(new(prodServ, "Regla Tipo = VTE"), new(unidad, "Regla Tipo = VTE"), new(obj, "Regla Tipo = VTE"));

    private static ProductoFiscalErp Producto(string? prodServ = "30171706", string? unidad = "MTK", string? obj = "02", bool intencional = false) =>
        new(Id, "13", "VIDRIO TEMPLADO DEMO", prodServ, unidad, obj, intencional);

    [Fact]
    public void CA_c_articulo_sin_clave_SAT_en_el_ERP_aparece_como_Faltante()
    {
        var d = ComparadorDiferenciasFiscales.CompararProducto(Producto(prodServ: null), Esperado()).Should().ContainSingle().Subject;
        (d.Campo, d.Estado, d.ValorOrigen, d.ValorErp, d.Fuente)
            .Should().Be((C.ClaveProdServSat, EstadoDiferenciaFiscal.Faltante, "30171706", null, "Regla Tipo = VTE"));
    }

    [Fact]
    public void Articulo_sin_regla_y_sin_clave_tambien_es_Faltante_y_con_clave_no_se_compara()
    {
        var sinRegla = new EsperadoFiscalProducto(new(null, "Sin regla fiscal"), new(null, "Sin regla ni mapeo de unidad"), new(null, "Sin regla fiscal"));
        ComparadorDiferenciasFiscales.CompararProducto(Producto(prodServ: null), sinRegla)
            .Should().ContainSingle().Which.Estado.Should().Be(EstadoDiferenciaFiscal.Faltante);
        ComparadorDiferenciasFiscales.CompararProducto(Producto(), sinRegla).Should().BeEmpty();
    }

    [Fact]
    public void Articulo_distinto_de_la_regla_es_Diferente_salvo_excepcion_o_ficha_confirmada()
    {
        ComparadorDiferenciasFiscales.CompararProducto(Producto(unidad: "H87"), Esperado())
            .Should().ContainSingle().Which.Estado.Should().Be(EstadoDiferenciaFiscal.Diferente);
        ComparadorDiferenciasFiscales.CompararProducto(Producto(unidad: "H87", intencional: true), Esperado()).Should().BeEmpty();
        ComparadorDiferenciasFiscales.CompararProducto(Producto(unidad: null, intencional: true), Esperado())
            .Should().ContainSingle().Which.Estado.Should().Be(EstadoDiferenciaFiscal.Faltante);
    }

    [Fact]
    public void Esperado_usa_la_regla_mas_especifica_luego_la_configuracion_y_el_mapeo_de_unidad()
    {
        var p = new ProductoAw(Guid.CreateVersion7(), "13", "VIDRIO TEMPLADO DEMO", "M2");
        p.AplicarClasificacion(null, "Templado", "VTE", "370");
        var ficha = new FichaFiscalProducto("30171706", "H87", "02", 0.16m);
        var porTipo = new ReglaFiscalAw(Guid.CreateVersion7(), "Tipo", "VTE", "Demo", ficha);
        var porWgr = new ReglaFiscalAw(Guid.CreateVersion7(), "Wgr", "370", "Demo", ficha with { ClaveProdServSat = "30171710" });
        var mapeo = new Dictionary<string, string> { ["M2"] = "MTK" };
        var config = new Dictionary<string, ReglaFiscalProductoAw> { ["vte"] = new() { ClaveProdServSat = "01010101", ObjetoImp = "02" } };

        var conReglas = DiferenciasFiscalesAwService.Esperado(p, [porTipo, porWgr], config, mapeo);
        conReglas.ClaveProdServSat.Should().Be(new ValorEsperado("30171710", "Regla Wgr = 370"));
        conReglas.ClaveUnidadSat.Valor.Should().Be("H87"); // la regla manda sobre el mapeo, igual que la sincronización

        var soloConfig = DiferenciasFiscalesAwService.Esperado(p, [], config, mapeo);
        soloConfig.ClaveProdServSat.Should().Be(new ValorEsperado("01010101", "Configuración Tipo = VTE"));
        soloConfig.ClaveUnidadSat.Should().Be(new ValorEsperado("MTK", "Mapeo de unidad M2"));

        var nada = DiferenciasFiscalesAwService.Esperado(new ProductoAw(Guid.CreateVersion7(), "47", "ROLLO DEMO", "ROLLO"), [], config, mapeo);
        nada.ClaveProdServSat.Valor.Should().BeNull();
        nada.ClaveUnidadSat.Fuente.Should().Be("Sin regla ni mapeo de unidad");
    }

    // ---------- programación ----------

    [Fact]
    public void Proxima_ejecucion_cae_en_el_dia_y_hora_locales_y_nunca_en_el_pasado()
    {
        var mx = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"); // UTC-6 sin horario de verano
        var hora = new TimeOnly(6, 0);
        // Domingo 18-oct-2026 10:00 local → lunes 19-oct 06:00 local = 12:00 UTC.
        DiferenciasFiscalesAwWorker.ProximaEjecucion(new DateTimeOffset(2026, 10, 18, 16, 0, 0, TimeSpan.Zero), DayOfWeek.Monday, hora, mx)
            .Should().Be(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));
        // Justo a la hora: la siguiente es la semana próxima.
        DiferenciasFiscalesAwWorker.ProximaEjecucion(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero), DayOfWeek.Monday, hora, mx)
            .Should().Be(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
    }
}
