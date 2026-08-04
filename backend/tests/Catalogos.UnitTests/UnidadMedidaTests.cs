using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.UnitTests;

/// <summary>
/// Tests de dominio de <see cref="UnidadMedida"/> (ADR-0046 Etapa 1a). El
/// guardrail de reconfiguración de conversión (UNIDAD_MEDIDA_EN_USO) solo se
/// puede ejercitar a nivel dominio en 1a: vía HTTP <c>estaEnUso</c> es siempre
/// false (nadie referencia la unidad todavía; el FK es Etapa 1b).
/// </summary>
public class UnidadMedidaTests
{
    private static UnidadMedida NuevaPza() =>
        new(Guid.NewGuid(), "PZA", "Pieza", DimensionUnidad.Conteo, 1m, 0, esBase: true);

    [Fact]
    public void Constructor_AsignaCampos()
    {
        var um = new UnidadMedida(
            Guid.NewGuid(), "KG", "Kilogramo", DimensionUnidad.Peso, 1m, 3, esBase: true);

        um.Codigo.Should().Be("KG");
        um.Nombre.Should().Be("Kilogramo");
        um.Dimension.Should().Be(DimensionUnidad.Peso);
        um.FactorABase.Should().Be(1m);
        um.Decimales.Should().Be(3);
        um.EsBase.Should().BeTrue();
        um.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void Constructor_RechazaFactorNoPositivo()
    {
        var act = () => new UnidadMedida(
            Guid.NewGuid(), "X", "Equis", DimensionUnidad.Peso, 0m, 0, esBase: false);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UNIDAD_MEDIDA_FACTOR_INVALIDO");
    }

    [Fact]
    public void Constructor_RechazaDecimalesFueraDeRango()
    {
        var act = () => new UnidadMedida(
            Guid.NewGuid(), "X", "Equis", DimensionUnidad.Peso, 1m, 7, esBase: false);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UNIDAD_MEDIDA_DECIMALES_INVALIDOS");
    }

    [Fact]
    public void ActualizarDatos_CambiaNombreYDecimales_SiemprePermitido()
    {
        var um = NuevaPza();

        um.ActualizarDatos(nombre: "Pieza unitaria", decimales: 2);

        um.Nombre.Should().Be("Pieza unitaria");
        um.Decimales.Should().Be(2);
        // No tocó la conversión ni el estatus.
        um.Dimension.Should().Be(DimensionUnidad.Conteo);
        um.FactorABase.Should().Be(1m);
        um.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void CambiarEstatus_CambiaEstatus()
    {
        var um = NuevaPza();

        um.CambiarEstatus(EstatusCatalogo.Inactivo);

        um.Estatus.Should().Be(EstatusCatalogo.Inactivo);
    }

    [Fact]
    public void ReconfigurarConversion_EnUso_LanzaGuardrail_YNoMuta()
    {
        var um = NuevaPza();

        var act = () => um.ReconfigurarConversion(
            DimensionUnidad.Peso, 5m, esBase: false, estaEnUso: true);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UNIDAD_MEDIDA_EN_USO");
        // El estado original se conserva.
        um.Dimension.Should().Be(DimensionUnidad.Conteo);
        um.FactorABase.Should().Be(1m);
        um.EsBase.Should().BeTrue();
    }

    [Fact]
    public void ReconfigurarConversion_NoEnUso_AplicaCambio()
    {
        var um = NuevaPza();

        um.ReconfigurarConversion(DimensionUnidad.Peso, 5m, esBase: false, estaEnUso: false);

        um.Dimension.Should().Be(DimensionUnidad.Peso);
        um.FactorABase.Should().Be(5m);
        um.EsBase.Should().BeFalse();
    }

    [Fact]
    public void ReconfigurarConversion_NoEnUso_RechazaFactorNoPositivo()
    {
        var um = NuevaPza();

        var act = () => um.ReconfigurarConversion(
            DimensionUnidad.Conteo, 0m, esBase: true, estaEnUso: false);

        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("UNIDAD_MEDIDA_FACTOR_INVALIDO");
    }
}
