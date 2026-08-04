using Millet.Integraciones.Fiscal.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Domain;

public sealed class RfcReceptorTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Constructor_normaliza_rfc_a_uppercase_y_trim()
    {
        var rfc = new RfcReceptor(Guid.NewGuid(), EmpresaId, "  mil010101aaa  ");

        rfc.Rfc.Should().Be("MIL010101AAA");
        rfc.DescargaHabilitada.Should().BeTrue();
        rfc.RefreshHabilitada.Should().BeTrue();
        rfc.CheckpointDescargaAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_rechaza_empresa_vacia()
    {
        var act = () => new RfcReceptor(Guid.NewGuid(), Guid.Empty, "MIL010101AAA");

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("RFC_RECEPTOR_EMPRESA_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SHORT")]            // 5 chars
    [InlineData("WAYTOOLONGRFCXXX")] // > 13
    public void Constructor_rechaza_rfc_invalido(string rfc)
    {
        var act = () => new RfcReceptor(Guid.NewGuid(), EmpresaId, rfc);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("RFC_RECEPTOR_RFC_INVALIDO");
    }

    [Theory]
    [InlineData("MIL010101AAA", 12)]
    [InlineData("MEXICOFIS010", 12)]
    [InlineData("ABCD850101XYZ", 13)] // persona física
    public void Constructor_acepta_rfc_de_12_o_13_chars(string rfc, int expectedLength)
    {
        var entity = new RfcReceptor(Guid.NewGuid(), EmpresaId, rfc);

        entity.Rfc.Should().HaveLength(expectedLength);
    }

    [Fact]
    public void DeshabilitarDescarga_no_afecta_refresh()
    {
        var rfc = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");

        rfc.DeshabilitarDescarga();

        rfc.DescargaHabilitada.Should().BeFalse();
        rfc.RefreshHabilitada.Should().BeTrue(); // independiente
    }

    [Fact]
    public void AvanzarCheckpoint_persiste_primer_valor()
    {
        var rfc = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
        var t1 = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

        rfc.AvanzarCheckpoint(t1);

        rfc.CheckpointDescargaAt.Should().Be(t1);
    }

    [Fact]
    public void AvanzarCheckpoint_no_retrocede()
    {
        var rfc = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
        var t1 = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);
        var antes = t1.AddMinutes(-30);

        rfc.AvanzarCheckpoint(t1);
        rfc.AvanzarCheckpoint(antes);

        rfc.CheckpointDescargaAt.Should().Be(t1); // sin cambio
    }

    [Fact]
    public void AvanzarCheckpoint_es_idempotente_con_mismo_valor()
    {
        var rfc = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
        var t1 = new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

        rfc.AvanzarCheckpoint(t1);
        rfc.AvanzarCheckpoint(t1);

        rfc.CheckpointDescargaAt.Should().Be(t1);
    }
}
