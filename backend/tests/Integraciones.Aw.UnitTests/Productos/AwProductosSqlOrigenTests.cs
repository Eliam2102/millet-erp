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
        AwProductosSqlOrigen.ConstruirFila(7, mcode, bloqueo, esp, alto, ancho, null, b1, b2, b3, unidad, capas);

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
