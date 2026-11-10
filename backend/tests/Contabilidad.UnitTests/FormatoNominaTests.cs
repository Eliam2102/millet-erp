using Millet.Contabilidad.Application.Nomina;
using Xunit;

namespace Millet.Contabilidad.UnitTests;

/// <summary>K10.11: lectura de importes y del lote del formato de nómina externa (supuesto VILO, D10).</summary>
public sealed class FormatoNominaTests
{
    [Theory]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("$ 1,234.50", 1234.50)]
    [InlineData("  ", 0)]
    [InlineData(null, 0)]
    [InlineData("-10", -10)]
    public void El_importe_admite_separador_de_miles_signo_de_pesos_y_vacio(string? texto, double esperado) =>
        Assert.Equal((decimal)esperado, FormatoNomina.Importe(texto));

    [Theory]
    [InlineData("diez")]
    [InlineData("1.2.3")]
    public void Un_importe_que_no_es_numero_se_reporta(string texto) => Assert.Null(FormatoNomina.Importe(texto));

    [Fact]
    public void La_misma_referencia_da_el_mismo_lote_sin_importar_mayusculas_acentos_ni_espacios()
    {
        Assert.Equal(FormatoNomina.IdLote("2026-Q22 Ordinaria"), FormatoNomina.IdLote("  2026-q22   ordinaria "));
        Assert.Equal(FormatoNomina.IdLote("Nómina Q22"), FormatoNomina.IdLote("nomina q22"));
        Assert.NotEqual(FormatoNomina.IdLote("2026-Q22 ordinaria"), FormatoNomina.IdLote("2026-Q23 ordinaria"));
    }

    [Fact]
    public void Las_columnas_obligatorias_tienen_alias_de_SAP_y_de_hoja_de_calculo()
    {
        Assert.Contains("debe", FormatoNomina.Alias["cargo"]);
        Assert.Contains("haber", FormatoNomina.Alias["abono"]);
        Assert.Contains("centro de costo", FormatoNomina.Alias["centro"]);
        Assert.Contains("codigo de nomina", FormatoNomina.Alias["empleado"]);
    }
}
