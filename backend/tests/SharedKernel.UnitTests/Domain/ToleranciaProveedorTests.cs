using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.UnitTests.Domain;

public sealed class ToleranciaProveedorTests
{
    [Fact]
    public void Proveedor_PermiteMontoCeroYVolverAGeneral()
    {
        var proveedor = new Proveedor(Guid.NewGuid(), "DEMO", "Proveedor DEMO", "DEMO010101AA1", TipoPersonaProveedor.Moral);
        Assert.Null(proveedor.ToleranciaFacturaContraOcMxn);
        proveedor.ActualizarToleranciaFacturaContraOc(5);
        Assert.Equal(5m, proveedor.ToleranciaFacturaContraOcMxn);
        proveedor.ActualizarToleranciaFacturaContraOc(0);
        Assert.Equal(0m, proveedor.ToleranciaFacturaContraOcMxn);
        proveedor.ActualizarToleranciaFacturaContraOc(null);
        Assert.Null(proveedor.ToleranciaFacturaContraOcMxn);
        Assert.Throws<BusinessRuleException>(() => proveedor.ActualizarToleranciaFacturaContraOc(-1));
        Assert.Throws<BusinessRuleException>(() => proveedor.ActualizarToleranciaFacturaContraOc(0.00001m));
        Assert.Throws<BusinessRuleException>(() => proveedor.ActualizarToleranciaFacturaContraOc(decimal.MaxValue));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1%")]
    [InlineData("1,50")]
    [InlineData("0.00001")]
    [InlineData("100000000000000")]
    public void ParametroGeneral_RechazaValorInvalido_SinCambiarAnterior(string valor)
    {
        var parametro = ParametroGlobal.CrearDeModulo(Guid.NewGuid(), ToleranciaFacturaContraOcParametro.Clave,
            "0.99", TipoParametro.Numero, "cxp", "Tolerancia factura contra OC");
        Assert.Throws<BusinessRuleException>(() => parametro.ActualizarValor(valor));
        Assert.Equal("0.99", parametro.Valor);
    }

    [Fact]
    public void ParametroGeneral_AceptaCeroYCambioA150()
    {
        Assert.Equal(0.99m, ToleranciaFacturaContraOcParametro.ValorPorOmision);
        var parametro = ParametroGlobal.CrearDeModulo(Guid.NewGuid(), ToleranciaFacturaContraOcParametro.Clave,
            "0.99", TipoParametro.Numero, "cxp", "Tolerancia factura contra OC");
        parametro.ActualizarValor("0");
        Assert.Equal(0m, ToleranciaFacturaContraOcParametro.LeerValor(parametro.Valor));
        parametro.ActualizarValor("1.50");
        Assert.Equal(1.50m, ToleranciaFacturaContraOcParametro.LeerValor(parametro.Valor));
    }
}
