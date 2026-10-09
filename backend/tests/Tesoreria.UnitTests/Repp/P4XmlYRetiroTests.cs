using System.Text;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Repp;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Tesoreria.UnitTests.Repp;
public sealed class P4XmlYRetiroTests
{
    private static readonly Guid Uuid = Guid.NewGuid(), Factura = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 10, 9);
    private static string Xml(string rfc="FIX010101ABC", string? factura=null, decimal importe=100, decimal saldo=0, string moneda="MXN", int parcialidad=1) => $"""
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:p="http://www.sat.gob.mx/Pagos20" xmlns:t="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="P" Fecha="2026-10-09T12:00:00">
          <cfdi:Emisor Rfc="{rfc}"/><cfdi:Complemento><t:TimbreFiscalDigital UUID="{Uuid}"/>
          <p:Pagos Version="2.0"><p:Pago FechaPago="2026-10-08T12:00:00" MonedaP="{moneda}" Monto="100">
          <p:DoctoRelacionado IdDocumento="{factura ?? Factura.ToString()}" MonedaDR="{moneda}" NumParcialidad="{parcialidad}" ImpSaldoAnt="100" ImpPagado="{importe.ToString(System.Globalization.CultureInfo.InvariantCulture)}" ImpSaldoInsoluto="{saldo.ToString(System.Globalization.CultureInfo.InvariantCulture)}"/>
          </p:Pago></p:Pagos></cfdi:Complemento></cfdi:Comprobante>
        """;
    [Fact]
    public void Xml_correcto_entrega_fecha_moneda_e_importe_del_pago() =>
        ReppXmlValidator.Validar(Encoding.UTF8.GetBytes(Xml()), Uuid, Fecha,"FIX010101ABC",Factura,"MXN").Single().Should().Be(new PagoFiscalRepp(new(2026,10,8),"MXN",100));
    [Theory] [InlineData("rfc")] [InlineData("factura")] [InlineData("importe")] [InlineData("saldo")] [InlineData("moneda")] [InlineData("parcialidad")] [InlineData("timbre")] [InlineData("fecha")] [InlineData("dtd")]
    public void Xml_mal_emitido_se_rechaza(string defecto)
    {
        var xml=defecto switch { "rfc"=>Xml(rfc:"OTR010101ABC"), "factura"=>Xml(factura:Guid.NewGuid().ToString()),
            "importe"=>Xml(importe:99,saldo:1), "saldo"=>Xml(saldo:10), "moneda"=>Xml(moneda:"USD"), "parcialidad"=>Xml(parcialidad:0),
            "dtd"=>"<!DOCTYPE foo [<!ENTITY xxe SYSTEM 'file:///no-leer'>]>"+Xml(), _=>Xml() };
        Action validar=()=>ReppXmlValidator.Validar(Encoding.UTF8.GetBytes(xml),defecto=="timbre" ? Guid.NewGuid():Uuid,defecto=="fecha" ? Fecha.AddDays(1):Fecha,"FIX010101ABC",Factura,"MXN");
        validar.Should().Throw<BusinessRuleException>();
    }
    [Fact]
    public async Task Retiro_antes_de_autorizacion_conserva_bloqueo_y_deduplica_evento()
    {
        var contexto=new Contexto(); await using var db=new TesoreriaDbContext(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,contexto);
        var id=Guid.NewGuid(); var payload=new PasivoRetiradoDePagoPayload(contexto.Current!.Value,contexto.UtcNow,Guid.NewGuid(),Guid.NewGuid(),"MXN","FIX revisión CxP");
        var handler=new RetirarPasivoDePagoHandler(db,contexto); await handler.Handle(new(id,payload),default); await handler.Handle(new(id,payload),default);
        var pasivo=await db.PasivosPendientesPago.SingleAsync(); pasivo.PagoBloqueado.Should().BeTrue();
        pasivo.AceptarAutorizacion(contexto.UtcNow.AddMinutes(-1)).Should().BeFalse();
        Action pagar=()=>pasivo.AplicarPago(1); pagar.Should().Throw<BusinessRuleException>().Where(e=>e.Code=="PASIVO_NO_AUTORIZADO_CXP");
        pasivo.AceptarAutorizacion(contexto.UtcNow.AddMinutes(1)).Should().BeTrue(); pasivo.PagoBloqueado.Should().BeFalse();
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }
    private sealed class Contexto:ICurrentEmpresaContext,IClock
    { public Guid? Current{get;}=Guid.NewGuid(); public bool IsBypassed=>false; public IDisposable Bypass()=>throw new NotSupportedException(); public DateTimeOffset UtcNow=>new(2026,10,9,12,0,0,TimeSpan.Zero); }
}
