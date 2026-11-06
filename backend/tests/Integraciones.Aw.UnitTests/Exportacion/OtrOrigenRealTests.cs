using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.ComercioExterior.Application.Ports;
using Millet.Integraciones.Aw.Infrastructure.Exportacion;
namespace Millet.Integraciones.Aw.UnitTests.Exportacion;
public sealed class OtrOrigenRealTests
{
    private static IOtrEntradaReader? Real(bool encendido)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:Exportacion:EntradaHabilitada"] = encendido.ToString(),
            ["IntegracionesAw:Exportacion:ContratoI134Firmado"] = encendido.ToString(),
            ["ConnectionStrings:AwClientesDb"] = "Server=aw;Database=MILMAIN;Encrypt=True",
        }).Build();
        var sp = new ServiceCollection().AddAwOtrExportacion(config).BuildServiceProvider();
        using var scope = sp.CreateScope();
        return scope.ServiceProvider.GetKeyedService<IOtrEntradaReader>("Real");
    }
    [Fact] public void Con_las_opciones_encendidas_el_origen_Real_existe() => Assert.NotNull(Real(true));
    [Fact] public void Apagado_el_origen_Real_no_existe_y_el_selector_responde_CE_OTR_ORIGEN_NO_HABILITADO() => Assert.Null(Real(false));
}
public sealed class OtrLectorRealSqlTests
{
    [Fact]
    public void La_consulta_Real_solo_lee_las_tablas_de_OTR_y_no_usa_OPENQUERY_ni_escribe()
    {
        var sql = OtrTablasAdapter.CabeceraReal(new OtrOptions()) + " " + OtrTablasAdapter.LineasReal;
        Assert.StartsWith("SELECT", sql);
        Assert.Contains("OTR.SYSADM.OTR_ROUTES", sql); Assert.Contains("OTR.SYSADM.OTR_CONTAINERS_ACTIONS", sql); Assert.Contains("OTR.SYSADM.OTR_CONTAINER_POSITIONS", sql);
        Assert.DoesNotContain("OPENQUERY", sql);
        foreach (var palabra in new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "EXEC", "DROP", "ALTER", "INTO" })
            Assert.DoesNotMatch($@"\b{palabra}\b", sql);
    }
    [Fact]
    public void La_direccion_cae_a_LOCATION_ADDRESS_cuando_el_texto_viene_vacio()
        => Assert.Contains("COALESCE(NULLIF(A.LOCATION_ADDRESS_TEXT, ''), NULLIF(A.LOCATION_ADDRESS, ''), '')", OtrTablasAdapter.CabeceraReal(new OtrOptions()));
    [Fact]
    public void Los_estatus_son_opciones_y_la_cancelacion_no_se_inventa()
    {
        var porOmision = OtrTablasAdapter.CabeceraReal(new OtrOptions());
        Assert.Contains("ROUTE_STATUS IN (NULL)", porOmision);   // sin regla de cancelación: nada se cancela
        Assert.Contains("ROUTE_STATUS NOT IN (50)", porOmision);
        var configurado = OtrTablasAdapter.CabeceraReal(new OtrOptions { EstatusRutaCancelada = "900, 950", EstatusRutaExcluidos = "50,1000" });
        Assert.Contains("ROUTE_STATUS IN (900,950) THEN 'Cancelacion'", configurado);
        Assert.Contains("ROUTE_STATUS NOT IN (50,1000)", configurado);
    }
    [Theory]
    [InlineData("50", true)] [InlineData("", true)] [InlineData("50, 100", true)]
    [InlineData("50; DROP TABLE x", false)] [InlineData("-1", false)] [InlineData("a", false)]
    public void Las_listas_de_estatus_solo_aceptan_enteros(string texto, bool valida) => Assert.Equal(valida, OtrOptions.ListaValida(texto));
    [Fact]
    public void Las_opciones_Real_se_leen_de_IntegracionesAw__Exportacion()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:Exportacion:EstatusRutaCancelada"] = "900", ["IntegracionesAw:Exportacion:EntregaDesde"] = "2026-11-01", ["IntegracionesAw:Exportacion:UnidadLinea"] = "MTK",
        }).Build();
        var o = config.GetSection(OtrOptions.SectionName).Get<OtrOptions>()!;
        Assert.Equal("900", o.EstatusRutaCancelada); Assert.Equal(new DateOnly(2026, 11, 1), o.EntregaDesde); Assert.Equal("MTK", o.UnidadLinea); Assert.Equal("50", o.EstatusRutaExcluidos);
    }
}
