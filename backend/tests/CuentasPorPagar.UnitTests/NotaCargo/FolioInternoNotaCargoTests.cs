using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.NotaCargo;

public sealed class FolioInternoNotaCargoTests
{
    [Fact]
    public void FromAnioSecuencial_genera_formato_canonico()
    {
        var f = FolioInternoNotaCargo.FromAnioSecuencial(2026, 1);
        f.Valor.Should().Be("NCG-2026-000001");
        f.Anio.Should().Be(2026);
        f.Secuencial.Should().Be(1);
    }

    [Fact]
    public void FromAnioSecuencial_padding_secuencial_seis_digitos()
    {
        var f = FolioInternoNotaCargo.FromAnioSecuencial(2026, 42);
        f.Valor.Should().Be("NCG-2026-000042");
    }

    [Fact]
    public void FromAnioSecuencial_rechaza_anio_fuera_de_rango()
    {
        var act = () => FolioInternoNotaCargo.FromAnioSecuencial(2019, 1);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FOLIO_NCG_ANIO_INVALIDO");
    }

    [Fact]
    public void FromAnioSecuencial_rechaza_secuencial_cero()
    {
        var act = () => FolioInternoNotaCargo.FromAnioSecuencial(2026, 0);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FOLIO_NCG_SECUENCIAL_INVALIDO");
    }

    [Fact]
    public void FromAnioSecuencial_rechaza_secuencial_un_millon()
    {
        var act = () => FolioInternoNotaCargo.FromAnioSecuencial(2026, 1_000_000);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FOLIO_NCG_SECUENCIAL_INVALIDO");
    }

    [Fact]
    public void Parse_reconstruye_componentes_desde_string()
    {
        var f = FolioInternoNotaCargo.Parse("NCG-2026-000042");
        f.Anio.Should().Be(2026);
        f.Secuencial.Should().Be(42);
    }

    [Fact]
    public void Parse_rechaza_formato_invalido()
    {
        var act = () => FolioInternoNotaCargo.Parse("NCG-26-1");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FOLIO_NCG_FORMATO_INVALIDO");
    }

    [Fact]
    public void Parse_rechaza_vacio()
    {
        var act = () => FolioInternoNotaCargo.Parse("  ");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FOLIO_NCG_VACIO");
    }
}
