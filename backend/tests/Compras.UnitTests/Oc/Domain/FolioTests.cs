using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests unitarios del VO <see cref="Folio"/> de OC (diseño §4.3).
/// Formato canónico: <c>OC-{prefijoSucursal}{año}-{secuencial:6}</c>.
/// </summary>
public class FolioTests
{
    [Theory]
    [InlineData("OC-MID2026-000001")]
    [InlineData("OC-MID2026-999999")]
    [InlineData("OC-MX2026-000123")]
    [InlineData("OC-MEXC2026-000001")]
    public void Parse_ConFormatoValido_DevuelveFolio(string valor)
    {
        var folio = Folio.Parse(valor);

        Assert.Equal(valor, folio.Valor);
        Assert.Equal(valor, folio.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MID2026-000001")]        // sin prefijo OC-
    [InlineData("OC-M2026-000001")]        // prefijo sucursal de 1 char
    [InlineData("OC-MIDEXT2026-000001")]   // prefijo sucursal de 5 chars
    [InlineData("OC-mid2026-000001")]      // minúsculas
    [InlineData("OC-MID26-000001")]        // año de 2 dígitos
    [InlineData("OC-MID2026-00001")]       // secuencial de 5 dígitos
    [InlineData("OC-MID2026-0000001")]     // secuencial de 7 dígitos
    [InlineData("OC-MID2026-ABCDEF")]      // secuencial con letras
    [InlineData("RQ-MID2026-000001")]      // prefijo distinto de OC-
    [InlineData("oc-MID2026-000001")]      // prefijo en minúsculas
    public void Parse_ConFormatoInvalido_LanzaBusinessRuleException(string valor)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Folio.Parse(valor));
        Assert.Equal("FOLIO_OC_FORMATO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Parse_ConValorNull_LanzaBusinessRuleException()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Folio.Parse(null!));
        Assert.Equal("FOLIO_OC_FORMATO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Folios_ConMismoValor_SonIguales()
    {
        // Como record, la igualdad es por valor.
        var a = Folio.Parse("OC-MID2026-000001");
        var b = Folio.Parse("OC-MID2026-000001");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Folios_ConValoresDistintos_NoSonIguales()
    {
        var a = Folio.Parse("OC-MID2026-000001");
        var b = Folio.Parse("OC-MID2026-000002");

        Assert.NotEqual(a, b);
    }
}
