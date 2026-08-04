using Millet.Integraciones.Fiscal.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Domain;

public sealed class DownloadRuleExternaTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RfcReceptorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Constructor_setea_campos_y_estado_activa()
    {
        var rule = new DownloadRuleExterna(
            id: Guid.NewGuid(),
            empresaId: EmpresaId,
            rfcReceptorId: RfcReceptorId,
            ruleIdExterno: "655a06bd-2d7d-477e-99f8-18d8fba52e52",
            satQueryType: SatQueryType.Metadata,
            downloadType: DownloadType.Recibidos,
            satInvoiceStatus: SatInvoiceStatusFilter.Vigente);

        rule.EmpresaId.Should().Be(EmpresaId);
        rule.RfcReceptorId.Should().Be(RfcReceptorId);
        rule.RuleIdExterno.Should().Be("655a06bd-2d7d-477e-99f8-18d8fba52e52");
        rule.SatQueryType.Should().Be(SatQueryType.Metadata);
        rule.DownloadType.Should().Be(DownloadType.Recibidos);
        rule.SatInvoiceStatus.Should().Be(SatInvoiceStatusFilter.Vigente);
        rule.Activa.Should().BeTrue();
    }

    [Fact]
    public void Constructor_rechaza_empresa_vacia()
    {
        var act = () => new DownloadRuleExterna(
            Guid.NewGuid(), Guid.Empty, RfcReceptorId, "ext", SatQueryType.Metadata,
            DownloadType.Recibidos, SatInvoiceStatusFilter.Vigente);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DOWNLOAD_RULE_EMPRESA_INVALIDA");
    }

    [Fact]
    public void Constructor_rechaza_rule_id_externo_vacio()
    {
        var act = () => new DownloadRuleExterna(
            Guid.NewGuid(), EmpresaId, RfcReceptorId, "  ", SatQueryType.Metadata,
            DownloadType.Recibidos, SatInvoiceStatusFilter.Vigente);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "DOWNLOAD_RULE_ID_EXTERNO_INVALIDO");
    }

    [Fact]
    public void Desactivar_y_activar_alternan_flag()
    {
        var rule = NewRule();
        rule.Desactivar();
        rule.Activa.Should().BeFalse();
        rule.Activar();
        rule.Activa.Should().BeTrue();
    }

    private static DownloadRuleExterna NewRule() => new(
        Guid.NewGuid(), EmpresaId, RfcReceptorId, "ext",
        SatQueryType.Metadata, DownloadType.Recibidos, SatInvoiceStatusFilter.Vigente);
}
