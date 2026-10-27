using Millet.Facturacion.Domain.Activos;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Facturacion.UnitTests.Activos;
public sealed class VigenciaVentaTests
{
    [Fact]
    public void Autorizacion_no_vence_y_solo_admite_precio_y_factura_autorizados()
    {
        var fecha = new DateTimeOffset(2026,5,1,12,0,0,TimeSpan.Zero);
        var a = AutorizacionVentaActivo.Crear(Guid.NewGuid(),"AF-TRA-1","Camioneta",500000,250000,false,300000,Guid.NewGuid(),fecha,
            MotivoDisposicionActivo.Venta, "Transportes", "Factura de venta");
        a.ValidarParaTimbrar(300000,fecha.AddDays(40));
        Assert.Equal("AF_NUEVA_AUTORIZACION_REQUERIDA",
            Assert.Throws<BusinessRuleException>(() => a.ValidarParaTimbrar(299999,fecha.AddDays(40))).Code);
        var factura = Guid.NewGuid(); a.MarcarUsada(factura);
        a.ValidarParaTimbrar(300000,fecha.AddDays(40),factura);
        Assert.Throws<BusinessRuleException>(() => a.ValidarParaTimbrar(300000,fecha.AddDays(40),Guid.NewGuid()));
    }

    [Theory]
    [InlineData(0, "Área", "Factura", "AF_MOTIVO_REQUERIDO")]
    [InlineData(99, "Área", "Factura", "AF_MOTIVO_REQUERIDO")]
    [InlineData(5, " ", "Factura", "AF_SOLICITANTE_REQUERIDO")]
    [InlineData(5, "Área", " ", "AF_SOPORTE_REQUERIDO")]
    public void Rechaza_datos_incompletos(int motivo, string solicitante, string soporte, string codigo)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => AutorizacionVentaActivo.Crear(
            Guid.NewGuid(),"AF-1","Activo",100,0,false,100,Guid.NewGuid(),DateTimeOffset.UtcNow,
            (MotivoDisposicionActivo)motivo, solicitante, soporte));
        Assert.Equal(codigo, ex.Code);
    }
}
