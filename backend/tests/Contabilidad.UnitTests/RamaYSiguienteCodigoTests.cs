using Millet.Contabilidad.Application;

namespace Millet.Contabilidad.UnitTests;

/// <summary>Opción 2 del alta manual: código en la rama del padre y siguiente código sugerido. Datos FIX-*.</summary>
public class RamaYSiguienteCodigoTests
{
    private static readonly FormatoCatalogo F = new(CatalogoOpciones.Predeterminadas());

    [Theory]
    [InlineData("FIX-100.10.00.00", "FIX-100.10")]
    [InlineData("FIX-100.00.00.00", "FIX-100")]
    [InlineData("FIX-171.01.00.000", "FIX-171.01")]
    [InlineData("FIX-4.1", "FIX-4.1")]
    public void Prefijo_significativo_quita_los_segmentos_finales_en_cero(string codigo, string esperado) =>
        F.PrefijoSignificativo(codigo).Should().Be(esperado);

    [Theory]
    [InlineData("FIX-100.10.30.00", "FIX-100.10.00.00", true)]   // ancho fijo, hija directa
    [InlineData("FIX-100.10.00.00", "FIX-100.00.00.00", true)]
    [InlineData("FIX-4.1.1", "FIX-4.1", true)]                    // ancho libre
    [InlineData("FIX-100.20.10.00", "FIX-100.10.00.00", false)]  // otra rama
    [InlineData("FIX-100.10.30.00", "FIX-100.00.00.00", false)]  // salta un nivel
    [InlineData("FIX-100.105.00.00", "FIX-100.10.00.00", false)] // comparte texto pero no segmento
    [InlineData("FIX-100.10.00.00", "FIX-100.10.00.00", false)]  // es el mismo nivel
    public void Esta_en_rama(string codigo, string padre, bool esperado) =>
        F.EstaEnRama(codigo, padre).Should().Be(esperado);

    [Fact]
    public void Ancho_fijo_sin_hijas_propone_la_primera()
    {
        var (codigo, motivo) = F.SiguienteHijo("FIX-100.10.00.00", ["FIX-100.10.00.00"]);
        codigo.Should().Be("FIX-100.10.01.00");
        motivo.Should().BeNull();
    }

    [Fact]
    public void Ancho_fijo_propone_el_mayor_mas_uno_e_ignora_nietas_y_otras_ramas()
    {
        var (codigo, _) = F.SiguienteHijo("FIX-100.10.00.00",
            ["FIX-100.10.00.00", "FIX-100.10.01.00", "FIX-100.10.02.00", "FIX-100.10.02.07", "FIX-100.20.09.00"]);
        codigo.Should().Be("FIX-100.10.03.00");
    }

    [Fact]
    public void Las_inactivas_cuentan_como_usadas_porque_el_codigo_no_se_reutiliza()
    {
        // La lista de existentes incluye inactivas: el máximo considera la 05 aunque esté dada de baja.
        var (codigo, _) = F.SiguienteHijo("FIX-100.10.00.00", ["FIX-100.10.01.00", "FIX-100.10.05.00"]);
        codigo.Should().Be("FIX-100.10.06.00");
    }

    [Fact]
    public void Rama_llena_no_propone_y_explica()
    {
        var (codigo, motivo) = F.SiguienteHijo("FIX-100.10.00.00", ["FIX-100.10.99.00"]);
        codigo.Should().BeNull();
        motivo.Should().Contain("ya no tiene códigos libres");
    }

    [Fact]
    public void Ancho_libre_usa_el_formato_de_las_hermanas_y_sin_hermanas_no_supone()
    {
        F.SiguienteHijo("FIX-4", ["FIX-4", "FIX-4.1", "FIX-4.2"]).Codigo.Should().Be("FIX-4.3");
        var (codigo, motivo) = F.SiguienteHijo("FIX-4", ["FIX-4"]);
        codigo.Should().BeNull();
        motivo.Should().Contain("escriba el código");
    }

    [Fact]
    public void Lo_sugerido_siempre_cumple_la_regla_de_rama()
    {
        var (codigo, _) = F.SiguienteHijo("FIX-100.10.00.00", ["FIX-100.10.01.00"]);
        F.EstaEnRama(codigo!, "FIX-100.10.00.00").Should().BeTrue();
    }
}
