using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.P8;

/// <summary>Decisión del owner (17-oct): alerta de retenciones sin revisar bloquea «Autorizar para pago».</summary>
public sealed class RetencionesRevisionTests
{
    private static readonly RetencionConcepto[] SinReglas = [];

    [Fact]
    public void Sin_retencion_no_alerta_si_el_xml_no_trae_retenciones()
    {
        ComparadorRetenciones.Alerta(ComparadorRetenciones.ConceptoSinRetencion, 3150, 0, [], SinReglas).Should().BeNull();
        ComparadorRetenciones.Alerta(ComparadorRetenciones.ConceptoSinRetencion, 3150, 126, [new("002", .04m, 126)], SinReglas)
            .Should().Contain("Sin retención");
    }

    [Fact]
    public void Sin_concepto_solo_alerta_si_el_xml_trae_retenciones()
    {
        ComparadorRetenciones.Alerta(null, 3150, 0, null, SinReglas).Should().BeNull();
        ComparadorRetenciones.Alerta(null, 3150, 126, null, SinReglas).Should().StartWith("Por confirmar");
    }

    [Fact]
    public async Task Alerta_sin_revisar_bloquea_autorizar_y_la_revision_lo_libera()
    {
        await using var a = new P8Fixture();
        var f = a.Nueva();
        f.AsignarAlertaRetenciones("FIX difieren");

        var autorizar = () => f.Autorizar(null, DateTimeOffset.UtcNow);
        autorizar.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CXP_RETENCIONES_SIN_REVISAR");

        f.MarcarRetencionesRevisadas("FIX Fiscal confirma ISR", Guid.NewGuid(), DateTimeOffset.UtcNow);
        autorizar.Should().NotThrow();
        f.RetencionesRevisionMotivo.Should().Be("FIX Fiscal confirma ISR");
    }

    [Fact]
    public async Task Cambiar_la_alerta_borra_la_revision_y_no_se_revisa_sin_alerta()
    {
        await using var a = new P8Fixture();
        var f = a.Nueva();
        var revisar = () => f.MarcarRetencionesRevisadas("FIX motivo válido", null, DateTimeOffset.UtcNow);
        revisar.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CXP_RETENCIONES_SIN_ALERTA");

        f.AsignarAlertaRetenciones("FIX alerta 1");
        revisar();
        f.AsignarAlertaRetenciones("FIX alerta 2");
        f.RetencionesRevisadasEn.Should().BeNull();

        var motivoCorto = () => f.MarcarRetencionesRevisadas("ok", null, DateTimeOffset.UtcNow);
        motivoCorto.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CXP_RETENCIONES_MOTIVO_INVALIDO");
    }
}
