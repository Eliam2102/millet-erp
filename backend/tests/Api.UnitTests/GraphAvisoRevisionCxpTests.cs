using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth.Provisioning;
using Millet.CuentasPorPagar.Domain.Notificaciones;

namespace Millet.Api.UnitTests;

public sealed class GraphAvisoRevisionCxpTests
{
    [Fact]
    public void Html_codifica_los_datos_y_muestra_dias_y_nivel()
    {
        var n = new SlaRevisionNotificacion(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, 5,
            NivelEscalamientoSla.SlaVencido, "<b>A-1</b>", "Daños & defectos", true);

        var html = GraphAvisoRevisionCxp.ConstruirHtml(n);

        Assert.Contains("&lt;b&gt;A-1&lt;/b&gt;", html);
        Assert.Contains("&amp; defectos", html);
        Assert.Contains("<strong>5</strong> días hábiles", html);
        Assert.Contains("plazo vencido", html);
        Assert.DoesNotContain("<b>A-1</b>", html);
    }

    [Fact]
    public void Html_sin_folio_lo_dice()
    {
        var n = new SlaRevisionNotificacion(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 5,
            NivelEscalamientoSla.AvisoGerente, null, "Otros motivos", false);

        Assert.Contains("sin folio", GraphAvisoRevisionCxp.ConstruirHtml(n));
    }

    private static IConfiguration Config(params (string, string?)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))).Build();

    private static readonly (string, string?)[] EntraCompleto =
    [
        ("Auth:EntraId:TenantId", "t"), ("Auth:EntraId:ClientId", "c"),
        ("Auth:EntraId:ClientSecret", "s"), ("Auth:EntraId:SenderEmail", "erp@example.com"),
    ];

    [Fact]
    public void Graph_sin_Contador_General_no_arranca_con_mensaje_claro()
    {
        var cfg = Config([.. EntraCompleto, ("CuentasPorPagar:AvisosRevision:Proveedor", "Graph")]);

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddGraphAvisosRevisionCxp(cfg));

        Assert.Contains("ContadorGeneral", ex.Message);
    }

    [Fact]
    public void Graph_con_Contador_General_solo_invalido_no_arranca()
    {
        var cfg = Config([.. EntraCompleto, ("CuentasPorPagar:AvisosRevision:Proveedor", "Graph"),
            ("CuentasPorPagar:AvisosRevision:ContadorGeneral:0", "no-es-correo")]);

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddGraphAvisosRevisionCxp(cfg));
    }

    [Fact]
    public void Graph_sin_credenciales_de_Entra_no_arranca()
    {
        var cfg = Config(("CuentasPorPagar:AvisosRevision:Proveedor", "Graph"),
            ("CuentasPorPagar:AvisosRevision:ContadorGeneral:0", "laura@example.com"));

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddGraphAvisosRevisionCxp(cfg));

        Assert.Contains("SenderEmail", ex.Message);
    }

    [Fact]
    public void Graph_solo_necesita_Entra_y_Contador_sin_config_de_colaboradores()
    {
        var cfg = Config([.. EntraCompleto, ("CuentasPorPagar:AvisosRevision:Proveedor", "Graph"),
            ("CuentasPorPagar:AvisosRevision:ContadorGeneral:0", "laura@example.com")]);
        var services = new ServiceCollection();

        services.AddGraphAvisosRevisionCxp(cfg);

        Assert.Single(services);
    }

    [Fact]
    public void NoOp_no_registra_nada_ni_exige_config()
    {
        var services = new ServiceCollection();

        services.AddGraphAvisosRevisionCxp(Config());

        Assert.Empty(services);
    }
}
