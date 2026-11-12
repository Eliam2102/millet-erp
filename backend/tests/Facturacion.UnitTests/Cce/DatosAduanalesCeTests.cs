using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cce;
public sealed class DatosAduanalesCeTests
{
    [Fact]
    public void Partida_sin_revision_bloquea_y_datos_revisados_reemplazan_la_captura()
    {
        EmitirFacturaVentaCceLinea[] lineas = [new("CAPTURA", "OTRA", 10, 1, 10, true)];
        Assert.Throws<BusinessRuleException>(() => DatosAduanalesFactura.Aplicar(lineas, null));
        PartidaAduanalLectura[] revisadas = [new(1, "7007199900", "01")];
        var salida = DatosAduanalesFactura.Aplicar(lineas, revisadas);
        Assert.Equal("7007199900", salida[0].FraccionArancelaria); Assert.Equal("01", salida[0].UnidadAduana);
        Assert.Equal(10, salida[0].CantidadAduana);
    }

    [Fact]
    public void M158_fraccion_de_CE_con_8_digitos_se_rechaza_con_la_partida()
    {
        EmitirFacturaVentaCceLinea[] lineas = [new("7007199900", "01", 10, 1, 10, true), new("7007199900", "01", 5, 1, 5, true)];
        PartidaAduanalLectura[] revisadas = [new(1, "7007199900", "01"), new(2, "70071999", "06")];
        var ex = Assert.Throws<BusinessRuleException>(() => DatosAduanalesFactura.Aplicar(lineas, revisadas));
        Assert.Equal("CCE_FRACCION_INVALIDA", ex.Code);
        Assert.StartsWith("Partida 2:", ex.Message);
    }

    [Fact]
    public void M158_la_fraccion_del_cuerpo_no_cuenta_manda_la_de_CE()
    {
        EmitirFacturaVentaCceLinea[] lineas = [new("70071999", "06", 10, 1, 10, true)];
        var salida = DatosAduanalesFactura.Aplicar(lineas, [new(1, "7007199900", "01")]);
        Assert.Equal("7007199900", salida[0].FraccionArancelaria);
    }

    [Theory]
    [InlineData(null, "ROO", "77500", "USA", "la calle")]
    [InlineData("DEMO 1", null, "77500", "USA", "el estado")]
    [InlineData("DEMO 1", "ROO", " ", "USA", "el código postal")]
    [InlineData("DEMO 1", "ROO", "77500", "MEX", "el país")]
    public void M158_el_domicilio_incompleto_dice_que_campo_falta(string? calle, string? estado, string? cp, string pais, string falta)
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.ValidarDatosSatCce("A1", calle, estado, cp, pais));
        Assert.Equal("CCE_DOMICILIO_RECEPTOR_INCOMPLETO", ex.Code);
        Assert.StartsWith($"Falta {falta}", ex.Message);
    }

    [Fact]
    public void M158_domicilio_completo_pasa()
    {
        Millet.Facturacion.Application.Timbrado.CfdiEmisionBuilder.ValidarDatosSatCce("A1", "DEMO 1", "TX", "75001", "USA");
    }
}
