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
}
