using Millet.ComercioExterior.Application.PublicPorts;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Cce;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.UnitTests.Cce;

/// <summary>
/// H-U22 (86e40p363): el ERP rechaza antes de emitir (sin gastar folio) una partida cuya unidad aduanera no es la UMT de su fracción
/// arancelaria; el PAC lo rechazaba con CCE161. Caso real: fracción 7610100100 con unidad 06 falla, con 01 timbra.
/// </summary>
public sealed class UnidadAduanaUmtTests
{
    private const string Fraccion = "7610100100";

    [Fact]
    public void La_umt_se_busca_por_los_primeros_8_digitos_del_catalogo_SAT()
    {
        UmtFraccionArancelaria.Umt(Fraccion).Should().Be("01");
        UmtFraccionArancelaria.Umt("76101001").Should().Be("01");
        UmtFraccionArancelaria.Umt("8471300100").Should().Be("06");
        UmtFraccionArancelaria.Umt("0000000000").Should().BeNull("la fracción que el catálogo no trae no se valida");
        UmtFraccionArancelaria.Umt("ABC").Should().BeNull();
        UmtFraccionArancelaria.Umt(null).Should().BeNull();
    }

    [Fact]
    public void Emision_con_fraccion_7610100100_y_unidad_06_se_rechaza_con_la_partida_la_fraccion_y_la_umt()
    {
        EmitirFacturaVentaCceLinea[] lineas = [new(Fraccion, "06", 10, 1, 10, true)];
        var ex = Assert.Throws<BusinessRuleException>(() => DatosAduanalesFactura.Aplicar(lineas, [new(1, Fraccion, "06")]));
        Assert.Equal("CCE_UNIDAD_ADUANA_UMT", ex.Code);
        Assert.StartsWith("Partida 1:", ex.Message);
        Assert.Contains("unidad aduanera 06", ex.Message);
        Assert.Contains("UMT 01", ex.Message);
        Assert.Contains(Fraccion, ex.Message);
    }

    [Fact]
    public void Emision_con_fraccion_7610100100_y_unidad_01_pasa()
    {
        EmitirFacturaVentaCceLinea[] lineas = [new(Fraccion, "01", 10, 1, 10, true)];
        var salida = DatosAduanalesFactura.Aplicar(lineas, [new(1, Fraccion, "01")]);
        Assert.Equal("01", salida[0].UnidadAduana);
    }

    [Fact]
    public void Con_varias_partidas_el_error_nombra_la_que_no_coincide()
    {
        EmitirFacturaVentaCceLinea[] lineas = [new("7007199900", "01", 1, 1, 1, true), new(Fraccion, "01", 1, 1, 1, true)];
        var ex = Assert.Throws<BusinessRuleException>(() =>
            DatosAduanalesFactura.Aplicar(lineas, [new(1, "7007199900", "01"), new(2, Fraccion, "06")]));
        Assert.StartsWith("Partida 2:", ex.Message);
    }

    [Fact]
    public void Unidad_99_o_fraccion_fuera_del_catalogo_no_se_bloquea_la_decide_el_PAC()
    {
        Assert.Null(UmtFraccionArancelaria.UmtEsperadaSiNoCoincide(Fraccion, "99"));
        Assert.Null(UmtFraccionArancelaria.UmtEsperadaSiNoCoincide("0000000000", "06"));
        EmitirFacturaVentaCceLinea[] lineas = [new("0000000000", "06", 1, 1, 1, true)];
        var salida = DatosAduanalesFactura.Aplicar(lineas, [new(1, "0000000000", "06")]);
        Assert.Equal("06", salida[0].UnidadAduana);
    }

    [Fact]
    public void El_complemento_CCE_tambien_rechaza_la_linea_con_unidad_distinta_de_la_umt()
    {
        var cce = ComplementoCce.Crear(Guid.NewGuid(), "2", "FOB", 20m, "TAX-1", "USA");
        var act = () => cce.AgregarLinea(Fraccion, "06", 10m, 2m, 20m, false);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CCE_UNIDAD_ADUANA_UMT");
        cce.AgregarLinea(Fraccion, "01", 10m, 2m, 20m, false);
        cce.Lineas.Should().ContainSingle();
    }
}
