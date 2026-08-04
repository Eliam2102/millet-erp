using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Cfdi;

public sealed class UuidCfdiTests
{
    [Fact]
    public void Parse_normaliza_a_mayusculas()
    {
        var uuid = UuidCfdi.Parse("5fb0f1c2-3e2a-4f0f-9e2e-2c5c2b5c1a2b");
        uuid.Valor.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
    }

    [Fact]
    public void Parse_acepta_uuid_en_mayusculas()
    {
        var uuid = UuidCfdi.Parse("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        uuid.Valor.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uuid")]
    [InlineData("5FB0F1C2-3E2A-4F0F-9E2E")]
    [InlineData("ZZZZZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZZZZZZZZZ")]
    public void Parse_rechaza_formatos_invalidos(string raw)
    {
        var act = () => UuidCfdi.Parse(raw);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void UuidCfdi_es_record_inmutable_con_value_equality()
    {
        var a = UuidCfdi.Parse("5fb0f1c2-3e2a-4f0f-9e2e-2c5c2b5c1a2b");
        var b = UuidCfdi.Parse("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        a.Should().Be(b);
    }
}
