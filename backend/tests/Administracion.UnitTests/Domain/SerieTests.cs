using Millet.Administracion.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Reglas de continuidad de series fiscales (U1.2, P04): folio inicial
/// capturado en el alta y series fiscales sin reinicio de periodo.
/// </summary>
public class SerieTests
{
    private static Serie Crear(
        TipoDocumentoSerie tipo = TipoDocumentoSerie.Cfdi,
        ReinicioPeriodo reinicio = ReinicioPeriodo.None,
        long folioInicial = 1) =>
        new(Guid.NewGuid(), Guid.NewGuid(), sucursalId: null, tipo, "DEMO", sufijo: null, reinicio, folioInicial);

    [Fact]
    public void Should_Default_FolioInicial_To_One()
    {
        Crear().FolioInicial.Should().Be(1);
    }

    [Fact]
    public void Should_Keep_Captured_FolioInicial()
    {
        Crear(folioInicial: 8201).FolioInicial.Should().Be(8201);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    [InlineData(long.MaxValue)]
    public void Should_Reject_Invalid_FolioInicial(long folio)
    {
        var act = () => Crear(folioInicial: folio);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SERIE_FOLIO_INICIAL_INVALIDO");
    }

    [Theory]
    [InlineData(TipoDocumentoSerie.Cfdi)]
    [InlineData(TipoDocumentoSerie.NotaCredito)]
    [InlineData(TipoDocumentoSerie.FacturaAnticipo)]
    public void Should_Reject_Period_Reset_On_Fiscal_Series(TipoDocumentoSerie tipo)
    {
        var act = () => Crear(tipo, ReinicioPeriodo.Anual);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SERIE_FISCAL_SIN_REINICIO");
    }

    [Theory]
    [InlineData(TipoDocumentoSerie.OrdenCompra)]
    [InlineData(TipoDocumentoSerie.Poliza)]
    public void Should_Allow_Period_Reset_On_Non_Fiscal_Series(TipoDocumentoSerie tipo)
    {
        Crear(tipo, ReinicioPeriodo.Mensual).ReinicioPeriodo.Should().Be(ReinicioPeriodo.Mensual);
    }
}
