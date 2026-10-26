using Millet.Facturacion.Domain.Activos;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Facturacion.UnitTests.Activos;
public sealed class VigenciaVentaTests
{
    [Theory]
    [InlineData(31, 300000)] [InlineData(30, 300000)] [InlineData(1, 299999)]
    public void CA8_Exige_nueva_autorizacion_por_vencimiento_o_precio(int dias, decimal precio)
    {
        var fecha = new DateTimeOffset(2026,5,1,12,0,0,TimeSpan.Zero);
        var a = AutorizacionVentaActivo.Crear(Guid.NewGuid(),"AF-TRA-1","Camioneta",500000,250000,false,300000,Guid.NewGuid(),fecha);
        Assert.Throws<BusinessRuleException>(() => a.ValidarParaTimbrar(precio,fecha.AddDays(dias)));
        a.ValidarParaTimbrar(300000,fecha.AddDays(29)); Assert.Equal(50000,a.UtilidadOPerdida);
        var factura = Guid.NewGuid(); a.MarcarUsada(factura); a.ValidarParaTimbrar(300000,fecha.AddDays(29),factura);
        Assert.Throws<BusinessRuleException>(() => a.ValidarParaTimbrar(300000,fecha.AddDays(29),Guid.NewGuid()));
    }
}
