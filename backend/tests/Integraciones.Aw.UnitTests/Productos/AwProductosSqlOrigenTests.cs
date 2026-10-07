using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;

namespace Millet.Integraciones.Aw.UnitTests.Productos;

/// <summary>Mapeo puro, composición, normalización de unidad, SQL estático y DI del lector SQL (ADM-07 paso G).</summary>
public sealed class AwProductosSqlOrigenTests
{
    private static string? Componer(params decimal?[] capas) => AwProductosSqlOrigen.ComponerComposicion(capas);

    [Fact]
    public void Composicion_une_espesores_con_mas_sin_ceros_sobrantes()
    {
        Componer(6m, 0.89m, 6m).Should().Be("6+0.89+6");
        Componer(3.00m, 12.0m, 3m).Should().Be("3+12+3");
    }

    [Fact]
    public void Composicion_ignora_capas_sin_espesor_y_sin_ninguna_es_null()
    {
        Componer(6m, 0m, 6m).Should().Be("6+6");
        Componer(6m, null, -1m, 6m).Should().Be("6+6");
        Componer().Should().BeNull();
        Componer(null, 0m).Should().BeNull();
    }

    private static AwProductoOrigenFila Fila(decimal? esp = 0, decimal? alto = 0, decimal? ancho = 0, int bloqueo = 0,
        string? b1 = "VIDRIO", string? b2 = null, string? b3 = null, string? mcode = "COD", string? unidad = "m²",
        params decimal?[] capas) =>
        AwProductosSqlOrigen.ConstruirFila(7, mcode, bloqueo, esp, alto, ancho, null, b1, b2, b3, unidad, capas, []);

    private static AwProductoOrigenFila ConMedidas(params (decimal? Ancho, decimal? Alto)[] medidas) =>
        AwProductosSqlOrigen.ConstruirFila(7, "DEMO-1", 0, 6m, 0m, 0m, null, "DEMO VIDRIO", null, null, "m²", [6m], medidas);

    [Fact]
    public void ConstruirFila_con_medidas_crea_una_variante_por_medida_sin_BASE()
    {
        var f = ConMedidas((1800m, 2600m), (2440m, 3660m), (3210m, 6000m));
        f.Variantes.Select(v => v.ClaveVariante).Should().Equal("1800x2600", "2440x3660", "3210x6000");
        f.Variantes.Should().OnlyContain(v => v.EspesorMm == 6m && v.Composicion == "6");
        var v1 = f.Variantes[0];
        (v1.AnchoMm, v1.AltoMm).Should().Be((1800m, 2600m));
    }

    [Fact]
    public void ConstruirFila_medidas_duplicadas_se_deduplican() =>
        ConMedidas((1800m, 2600m), (1800.0m, 2600m)).Variantes.Should().ContainSingle();

    [Fact]
    public void ConstruirFila_medidas_con_cero_o_null_se_ignoran_y_sin_ninguna_queda_BASE()
    {
        ConMedidas((0m, 2600m), (1800m, null), (1800m, 2600m)).Variantes.Should().ContainSingle()
            .Which.ClaveVariante.Should().Be("1800x2600");
        ConMedidas((0m, 2600m), (null, null)).Variantes.Should().ContainSingle()
            .Which.ClaveVariante.Should().Be("BASE");
    }

    [Fact]
    public void ConstruirFila_cero_en_medidas_es_null_y_crea_variante_BASE()
    {
        var f = Fila(esp: 6m);
        f.ProductoRef.Should().Be("7");
        var v = f.Variantes.Should().ContainSingle().Subject;
        (v.ClaveVariante, v.EspesorMm, v.AltoMm, v.AnchoMm, v.Composicion).Should().Be(("BASE", 6m, null, null, null));
    }

    [Fact]
    public void ConstruirFila_sin_medidas_ni_capas_no_tiene_variantes()
    {
        Fila().Variantes.Should().BeEmpty();
        Fila(esp: null, alto: null, ancho: null).Variantes.Should().BeEmpty();
        Fila(capas: [6m, 6m]).Variantes.Single().Composicion.Should().Be("6+6");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void ConstruirFila_KZ_GESPERRT_distinto_de_cero_es_baja(int bloqueo, bool baja) =>
        Fila(bloqueo: bloqueo).Baja.Should().Be(baja);

    [Fact]
    public void ConstruirFila_descripcion_une_BEZ_y_respalda_con_MCODE()
    {
        Fila(b1: " A ", b2: "", b3: "C").Descripcion.Should().Be("A C");
        Fila(b1: null, mcode: " COD ").Descripcion.Should().Be("COD");
        Fila(b1: " ", mcode: null).Descripcion.Should().BeNull();
    }

    [Fact]
    public void ConstruirFila_unidad_pasa_cruda_recortada()
    {
        Fila(unidad: " <indf> ").UnidadMedida.Should().Be("<indf>");
        Fila(unidad: null).UnidadMedida.Should().BeNull();
    }

    [Theory]
    [InlineData("m²", "M2")]
    [InlineData("m³", "M3")]
    [InlineData("m lin.", "M")]
    [InlineData("ltr", "L")]
    [InlineData("<indf>", null)]
    [InlineData("Pza", "PZA")]
    [InlineData("  ", null)]
    public void NormalizarUnidad_aplica_alias_de_A_W(string entrada, string? esperado) =>
        AwProductoSnapshotMapper.NormalizarUnidad(entrada).Should().Be(esperado);

    [Theory]
    [InlineData(AwProductosSqlOrigen.SqlPagina)]
    [InlineData(AwProductosSqlOrigen.SqlReferencia)]
    [InlineData(AwProductosSqlOrigen.SqlComposicion)]
    [InlineData(AwProductosSqlOrigen.SqlMedidas)]
    public void Sql_es_solo_lectura_con_columnas_explicitas(string sql)
    {
        sql.Should().StartWith("SELECT");
        sql.Replace(AwProductosSqlOrigen.MarcaTipos, "").Should().NotContain("*");
        sql.Should().NotMatchRegex(@"(?i)\b(STRING_AGG|STRING_SPLIT|INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|EXEC|OPENJSON|FOR\s+JSON)\b");
        sql.Should().Contain("SYSADM.").And.NotContain("dbo.");
    }

    [Fact]
    public void Sql_pagina_parametriza_lote_cursor_y_filtro_de_tipos()
    {
        AwProductosSqlOrigen.SqlPagina.Should().Contain("TOP (@n)").And.Contain("@cursor").And.Contain(AwProductosSqlOrigen.MarcaTipos);
        AwProductosSqlOrigen.SqlComposicion.Should().Contain("BOM_LEVEL = 1").And.Contain("@min").And.Contain("@max");
    }

    private static ServiceProvider Proveedor(string? cs, AwProductosOrigenTipo origen = AwProductosOrigenTipo.Sql)
    {
        var datos = new Dictionary<string, string?>
        {
            ["IntegracionesAw:Productos:OrigenHabilitado"] = "true",
            ["IntegracionesAw:Productos:Origen"] = origen.ToString(),
            ["ConnectionStrings:AwProductosDb"] = cs,
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(datos).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddIntegracionesAwProductos(config);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@Microsoft.KeyVault(SecretUri=https://kv.invalid/secrets/x)")]
    public void DI_Sql_sin_connection_string_real_no_registra_origen(string? cs) =>
        Proveedor(cs).GetService<IAwProductosOrigen>().Should().BeNull();

    [Fact]
    public void DI_Sql_con_TLS_verificado_registra_el_lector() =>
        Proveedor("Server=tcp:demo.invalid,1433;Database=D;User Id=u;Password=p;Encrypt=True;TrustServerCertificate=False")
            .GetService<IAwProductosOrigen>().Should().BeOfType<AwProductosSqlOrigen>();

    [Fact]
    public void DI_Sql_con_Encrypt_False_lanza()
    {
        var act = () => Proveedor("Server=tcp:demo.invalid,1433;Database=D;User Id=u;Password=p;Encrypt=False");
        act.Should().Throw<InvalidOperationException>().WithMessage("*AwProductosDb*TLS*");
    }
    // Árbol sintético con la forma real de un aislante (500083): TE + perfil + VLA con relleno y procesos.
    private static AwProductosSqlOrigen.PiezaCruda Pz(int nivel, int node, string @ref, string? tipo = null, string? bez = null, decimal? dicke = null) =>
        new(nivel, node, @ref, dicke, tipo, null, bez, null, null);

    private static readonly AwProductosSqlOrigen.PiezaCruda[] ArbolDemo =
    [
        Pz(1, 0, "370306", "VTE", "DEMO TEMPLADO", 6m), Pz(2, 1, "600000", "Proceso", "DEMO FILOS MUERTOS"),
        Pz(1, 0, "530012", "Perfil intercalario", "DEMO PERFIL"), Pz(1, 0, "410000", "VLA", "DEMO LAMINADO"),
        Pz(2, 4, "100004", "Vidrio plano", "DEMO FLOAT", 3m), Pz(3, 5, "600000", "Proceso"),
        Pz(2, 4, "419000", "Relleno", "DEMO RELLENO"), Pz(2, 4, "100004", "Vidrio plano", "DEMO FLOAT", 3m),
        Pz(3, 8, "600000", "Proceso"),
    ];

    [Fact]
    public void ConstruirComponentes_arma_el_arbol_por_posicion_con_procesos_incluidos()
    {
        var r = AwProductosSqlOrigen.ConstruirComponentes(ArbolDemo);

        r.Select(c => c.Orden).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
        r.Select(c => c.PadreOrden).Should().Equal(null, 1, null, null, 4, 5, 4, 4, 8);
        r.Select(c => c.Nivel).Should().Equal(1, 2, 1, 1, 2, 3, 2, 2, 3);
        r.Count(c => c.Tipo == "Proceso").Should().Be(3);
        r[0].Should().Be(new AwProductoOrigenComponente(1, 1, null, "370306", "DEMO TEMPLADO", "VTE", 6m));
        r[5].Descripcion.Should().BeNull();
        r[1].EspesorMm.Should().BeNull();
    }

    [Fact]
    public void ConstruirComponentes_usa_el_texto_resuelto_de_A_W_en_vez_de_la_plantilla()
    {
        var r = AwProductosSqlOrigen.ConstruirComponentes(
        [
            new(1, 0, "600000", null, "Proceso", null, "FILOS MUERTOS", null, "FM [<§>]  <%> FILO(S) MUERTO(S)", "FM [1/2/3/4]  4 FILO(S) MUERTO(S) "),
            new(1, 0, "600000", null, "Proceso", null, "FILOS MUERTOS", null, "FM [<§>]  <%> FILO(S) MUERTO(S)"),
        ]);

        r[0].Descripcion.Should().Be("FILOS MUERTOS FM [1/2/3/4] 4 FILO(S) MUERTO(S)");
        r[1].Descripcion.Should().Be("FILOS MUERTOS FM [<§>] <%> FILO(S) MUERTO(S)"); // sin BEA_TEXT: queda la plantilla
    }

    [Fact]
    public void ConstruirComponentes_padre_que_no_cumple_la_regla_queda_sin_padre()
    {
        var r = AwProductosSqlOrigen.ConstruirComponentes(
            [Pz(1, 0, "A"), Pz(2, 5, "B"), Pz(3, 1, "C"), Pz(2, 1, "D")]);

        // B apunta a una fila inexistente, C a una de nivel 1 (debía ser 2): sin padre. D sí cumple.
        r.Select(c => c.PadreOrden).Should().Equal(null, null, null, 1);
    }

    [Fact]
    public void ConstruirFila_incluye_clasificacion_y_arbol()
    {
        var f = AwProductosSqlOrigen.ConstruirFila(7, " VT6 ", 0, 6m, 0m, 0m, null, "DEMO", null, null, "m²", [], [],
            " Vidrio templado claro ", "VTE", ArbolDemo, " 370 ", " VIDRIO TEMPLADO CONTROL SOLAR ");

        (f.CodigoModelo, f.Grupo, f.Tipo).Should().Be(("VT6", "Vidrio templado claro", "VTE"));
        (f.Wgr, f.WgrDescripcion).Should().Be(("370", "VIDRIO TEMPLADO CONTROL SOLAR"));
        f.Componentes.Should().HaveCount(9);
        AwProductosSqlOrigen.ConstruirFila(7, null, 0, 0m, 0m, 0m, null, "DEMO", null, null, "m²", [], [])
            .Componentes.Should().BeEmpty();
    }

    [Fact]
    public void Mapper_pasa_clasificacion_y_componentes_y_rechaza_componente_sin_ref()
    {
        var f = AwProductosSqlOrigen.ConstruirFila(7, "VT6", 0, 6m, 0m, 0m, null, "DEMO", null, null, "m²", [], [], "G", "VLA", ArbolDemo, "37*", "TEMPLADO");
        var s = AwProductoSnapshotMapper.Mapear(f, DateTime.UtcNow).Snapshot!;
        (s.CodigoModelo, s.Grupo, s.Tipo).Should().Be(("VT6", "G", "VLA"));
        (s.Wgr, s.WgrDescripcion).Should().Be(("37*", "TEMPLADO"));
        s.Componentes.Should().HaveCount(9);
        s.Componentes![4].PadreOrden.Should().Be(4);

        var mala = f with { Componentes = [new(1, 1, null, " ", "x", null, null)] };
        AwProductoSnapshotMapper.Mapear(mala, DateTime.UtcNow).Error.Should().Contain("componente sin ref");
    }

    // Referencia no válida o 0 (registro nulo de A+W): responde null SIN abrir conexión.
    private sealed class FabricaQueNoSeUsa : IIntegracionSqlConnectionFactory
    {
        public System.Data.Common.DbConnection CreateConnection() => throw new InvalidOperationException("No debe conectar.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("")]
    public async Task LeerPorReferencia_invalida_o_cero_devuelve_null_sin_conectar(string referencia)
    {
        var origen = new AwProductosSqlOrigen(new FabricaQueNoSeUsa(),
            Microsoft.Extensions.Options.Options.Create(new AwProductosOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AwProductosSqlOrigen>.Instance);
        (await origen.LeerPorReferenciaAsync(referencia, CancellationToken.None)).Should().BeNull();
    }
}
