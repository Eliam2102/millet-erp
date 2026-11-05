using Millet.Facturacion.Domain.Activos;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Facturacion.UnitTests.Activos;
public sealed class P17RechazoTests
{
    [Fact] public void Solo_autorizador_cierra_venta_sin_usar_con_motivo_y_conserva_historial()
    {
        var daf=Guid.NewGuid();var ahora=DateTimeOffset.UtcNow;
        var a=AutorizacionVentaActivo.Crear(Guid.NewGuid(),"P17","Equipo",1000,100,false,1200,daf,ahora,MotivoDisposicionActivo.Venta,"Área usuaria","Factura");
        Assert.Throws<BusinessRuleException>(()=>a.Rechazar(daf,"",ahora));
        Assert.Throws<BusinessRuleException>(()=>a.Rechazar(Guid.NewGuid(),"No procede",ahora));
        a.Rechazar(daf,"No procede",ahora);Assert.Equal(EstadoAutorizacionActivo.Cancelada,a.Estado);Assert.Equal("No procede",a.MotivoRechazo);Assert.Equal(daf,a.RechazadoPor);Assert.Equal(ahora,a.RechazadoEn);
        Assert.Throws<BusinessRuleException>(()=>a.ValidarParaTimbrar(1200,ahora));
    }
    [Fact] public void El_registrador_de_venta_debe_ser_distinto_del_daf()
    {
        var daf=Guid.NewGuid();var a=AutorizacionVentaActivo.Crear(Guid.NewGuid(),"P17","Equipo",1000,100,false,1200,daf,DateTimeOffset.UtcNow,MotivoDisposicionActivo.Venta,"Área usuaria","Factura");
        Assert.Throws<BusinessRuleException>(()=>a.ValidarRegistrador(daf));Assert.Throws<BusinessRuleException>(()=>a.ValidarRegistrador(null));a.ValidarRegistrador(Guid.NewGuid());
    }
    [Fact] public void Venta_usada_no_puede_rechazarse()
    {
        var daf=Guid.NewGuid();var ahora=DateTimeOffset.UtcNow;
        var a=AutorizacionVentaActivo.Crear(Guid.NewGuid(),"P17","Equipo",1000,100,false,1200,daf,ahora,MotivoDisposicionActivo.Venta,"Área usuaria","Factura");
        a.MarcarUsada(Guid.NewGuid());Assert.Throws<BusinessRuleException>(()=>a.Rechazar(daf,"No procede",ahora));
    }
}
