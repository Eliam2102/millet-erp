using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.Tesoreria.Infrastructure.Persistence;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Application.EventListeners;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;
using Anticipo = Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
using Cargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo;
namespace Millet.Api.IntegrationTests.CuentasPorPagar;
/// <summary>HTTP real y PostgreSQL desechable; documentos FIX-P4, maestros simulados, limpieza por proveedor único.</summary>
public sealed class P4EndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Cxp="/api/v1/cuentas-por-pagar";
    private WebApplicationFactory<Program> App(Puertos p) => factory.ConEmpresa(EmpresaBootstrapId).WithWebHostBuilder(b=>b.ConfigureTestServices(s=>{
        s.RemoveAll<IProveedorReadPort>();s.AddSingleton<IProveedorReadPort>(p);
        s.RemoveAll<Millet.Tesoreria.Domain.Ports.IReppXmlBlobStorage>();s.AddSingleton<Millet.Tesoreria.Domain.Ports.IReppXmlBlobStorage, Blob>();
        s.RemoveAll<Millet.Tesoreria.Domain.Ports.DatosMaestros.IProveedorBancoReadPort>();s.AddSingleton<Millet.Tesoreria.Domain.Ports.DatosMaestros.IProveedorBancoReadPort>(p);
        s.RemoveAll<IPeriodoContablePort>();s.AddSingleton<IPeriodoContablePort>(p);
        s.RemoveAll<IDependenciaRevisoraReadPort>();s.AddSingleton<IDependenciaRevisoraReadPort>(p);
        s.RemoveAll<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort>();s.AddSingleton<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort>(p);
    }));
    [Fact]
    public async Task Cargo_formalizacion_aplicaciones_cancelaciones_y_serie_persisten_con_outbox()
    {
        var p=new Puertos();using var app=App(p);var client=await LoginAsync(app);
        try {
            Guid fId,cargoId,ncId,antiId,nc07Id,anti07Id,ncManualId;int cargoV,ncV,antiV,nc07V,anti07V,ncManualV;
            using(var scope=app.Services.CreateScope()) {
                var db=scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();var f=p.NuevaFactura();f.Autorizar(null,p.Ahora);
                var cargo=p.NuevaCargo(f.Id,10000);cargo.Autorizar(null,p.Ahora);var nc=p.NuevaNc(f.Id,10000,TipoRelacionCfdi.Devolucion,f.UuidCfdi!);
                var anti=p.NuevaAnticipo();var anti07=p.NuevaAnticipo();var nc07=p.NuevaNc(null,100,TipoRelacionCfdi.AmortizacionAnticipo,anti07.UuidCfdi);
                var ncManual=p.NuevaNc(null,100,TipoRelacionCfdi.NotaCredito,Guid.NewGuid().ToString());
                db.AddRange(f,cargo,nc,anti,anti07,nc07,ncManual);await db.SaveChangesAsync();
                fId=f.Id;cargoId=cargo.Id;cargoV=cargo.Version;ncId=nc.Id;ncV=nc.Version;antiId=anti.Id;antiV=anti.Version;
                nc07Id=nc07.Id;nc07V=nc07.Version;anti07Id=anti07.Id;anti07V=anti07.Version;ncManualId=ncManual.Id;ncManualV=ncManual.Version;
            }
            Assert.Equal(HttpStatusCode.OK,(await Post(client,$"/notas-cargo/{cargoId}/aplicar",new {},cargoV)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await Post(client,$"/notas-cargo/{cargoId}/formalizar",new {notaCreditoId=ncId},await Version(app,cargoId,true))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await Post(client,$"/facturas/{fId}/aplicar-nc",new {notaCreditoId=ncId,notaCreditoVersionEsperada=ncV,monto=10000},await Version(app,fId))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await Post(client,$"/facturas/{fId}/aplicar-anticipo",new {anticipoId=antiId,anticipoVersionEsperada=antiV,monto=100},await Version(app,fId))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await Post(client,$"/anticipos/{anti07Id}/amortizar-nc",new {notaCreditoId=nc07Id,ncVersionEsperada=nc07V,monto=100},anti07V)).StatusCode);
            var mismatch=await Post(client,$"/notas-credito/{ncManualId}/vincular-factura",new {facturaOrigenId=fId},ncManualV);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,mismatch.StatusCode);Assert.Equal("NC_UUID_RELACION_DISTINTO",await Code(mismatch));
            Assert.Equal(HttpStatusCode.OK,(await Post(client,$"/notas-credito/{ncManualId}/vincular-factura",new {facturaOrigenId=fId,excepcionRelacion=true,motivoExcepcion="FIX excepción verificada"},ncManualV)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await Post(client,$"/notas-credito/{ncManualId}/cancelar",new {motivo="FIX cancelación"},await VersionNc(app,ncManualId))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await Send(client,HttpMethod.Put,Cxp+$"/anticipos/serie/{p.Proveedor}",new {serie="FANTP4"})).StatusCode);
            Assert.Equal("FANTP4",(await Json(await client.GetAsync(Cxp+$"/anticipos/serie/{p.Proveedor}"))).GetProperty("serie").GetString());
            using var verificar=app.Services.CreateScope();var final=verificar.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var factura=await final.FacturasProveedor.FindAsync(fId);Assert.Equal(39900,factura!.SaldoPendiente);Assert.Equal(10000,factura.CargosAplicadosTotal);Assert.Equal(0,factura.NcAplicadasTotal);
            Assert.Equal(EstadoNotaCargo.Formalizada,(await final.NotasCargo.FindAsync(cargoId))!.Estado);
            var eventos=await final.OutboxEntries.Where(o=>o.EventType=="cuentas_por_pagar.pasivo.autorizado-para-pago.v1").ToListAsync();
            Assert.Contains(eventos,o=>o.Payload.Contains(fId.ToString(),StringComparison.Ordinal));
        } finally {await Limpiar(app,p);}
    }
    [Fact]
    public async Task Enviar_factura_a_revision_publica_retiro_y_Tesoreria_rechaza_pago_422()
    {
        var p=new Puertos();using var app=App(p);var client=await LoginAsync(app);Guid cuentaId=Guid.Empty,facturaId=Guid.Empty;
        try {
            int version;using(var scope=app.Services.CreateScope()) {
                var cxp=scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();var f=p.NuevaFactura();f.Autorizar(null,p.Ahora);cxp.Add(f);await cxp.SaveChangesAsync();facturaId=f.Id;version=f.Version;
                var tes=scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();var cuenta=new CuentaBancaria(EmpresaBootstrapId,"FIX-P4","0000000001",null,"MXN");
                tes.AddRange(cuenta,new PasivoPendientePago(EmpresaBootstrapId,f.Id,p.Proveedor,null,50000,50000,"MXN",null,new(2026,11,1),Guid.Parse(f.UuidCfdi!),"FIX-P4",p.Ahora,"PPD"));await tes.SaveChangesAsync();cuentaId=cuenta.Id;
            }
            var retiro=await Post(client,$"/facturas/{facturaId}/enviar-revision",new {motivoRevisionId=Guid.Parse("00000007-1001-0000-0000-00000000000e"),dependenciaRevisoraId=p.Dependencia},version);
            Assert.Equal(HttpStatusCode.OK,retiro.StatusCode);
            // Incluso con la proyección desactualizada, el puerto directo de P3 niega el pago.
            var body=new {cuentaBancariaId=cuentaId,fechaValor="2026-10-09",aplicaciones=new[]{new {facturaProveedorId=facturaId,importe=100m}}};
            var bloqueado=await client.PostAsJsonAsync("/api/v1/tesoreria/pagos/",body);Assert.Equal(HttpStatusCode.UnprocessableEntity,bloqueado.StatusCode);Assert.Equal("PASIVO_NO_PAGABLE_CXP",await Code(bloqueado));
            using(var scope=app.Services.CreateScope()) {
                var cxp=scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();var retiros=await cxp.OutboxEntries.Where(e=>e.EventType==PasivoRetiradoDePagoPayload.EventType).ToListAsync();Assert.Contains(retiros,e=>e.Payload.Contains(facturaId.ToString(),StringComparison.Ordinal));
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RetirarPasivoDePagoCommand(Guid.NewGuid(),new(EmpresaBootstrapId,p.Ahora.AddDays(1),facturaId,p.Proveedor,"MXN","FIX revisión")));
            }
            var local=await client.PostAsJsonAsync("/api/v1/tesoreria/pagos/",body);Assert.Equal(HttpStatusCode.UnprocessableEntity,local.StatusCode);Assert.Equal("PASIVO_NO_AUTORIZADO_CXP",await Code(local));
        } finally {
            using(var scope=app.Services.CreateScope()) {var tes=scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();await tes.PasivosPendientesPago.Where(x=>x.FacturaProveedorId==facturaId).ExecuteDeleteAsync();await tes.CuentasBancarias.Where(x=>x.Id==cuentaId).ExecuteDeleteAsync();}
            await Limpiar(app,p);
        }
    }
    [Fact]
    public async Task REPP_mal_emitido_rechaza_y_parcialidades_liberan_factura_pagada_solo_al_cubrir_pago()
    {
        var p=new Puertos();using var app=App(p);var client=await LoginAsync(app);
        Guid facturaId=Guid.Empty,cuentaId=Guid.Empty,movimientoId=Guid.Empty,pagoId=Guid.Empty;string uuidFactura="";
        try {
            using(var scope=app.Services.CreateScope()) {
                var cxp=scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
                var f=p.NuevaFactura(); f.AsignarMetodoPago("PPD");f.Autorizar(null,p.Ahora);f.RegistrarPago(50000,p.Ahora,"FIX-P4");cxp.Add(f);await cxp.SaveChangesAsync();facturaId=f.Id;uuidFactura=f.UuidCfdi!;
                var tes=scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();var cuenta=new CuentaBancaria(EmpresaBootstrapId,"FIX-P4","0000000001",null,"MXN");
                var movimiento=Millet.Tesoreria.Domain.Movimientos.MovimientoBancario.RegistrarPagoProveedor(EmpresaBootstrapId,cuenta,p.Proveedor,50000,new(2026,9,11),null,null,Guid.NewGuid(),p.Ahora);
                var pago=new Millet.Tesoreria.Domain.Movimientos.AplicacionPagoProveedor(movimiento.Id,f.Id,p.Proveedor,50000,p.Ahora);
                tes.AddRange(cuenta,movimiento,pago,new PasivoPendientePago(EmpresaBootstrapId,f.Id,p.Proveedor,null,50000,0,"MXN",null,new(2026,11,1),Guid.Parse(uuidFactura),"FIX-P4",p.Ahora,"PPD"));await tes.SaveChangesAsync();cuentaId=cuenta.Id;movimientoId=movimiento.Id;pagoId=pago.Id;
                var calendario=scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.Calendario.ICalendarioHabil>();
                var pagos=scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Domain.Ports.Tesoreria.IPagosProveedorReadPort>();
                var quinto=new Millet.CuentasPorPagar.Application.FacturaProveedor.RevisionRepp.RevisarFaltaReppHandler(cxp,new Reloj(new(2026,9,21)),calendario,pagos);
                Assert.Equal(0,await quinto.Handle(new(),default));
                var sexto=new Millet.CuentasPorPagar.Application.FacturaProveedor.RevisionRepp.RevisarFaltaReppHandler(cxp,new Reloj(new(2026,9,22)),calendario,pagos);
                Assert.Equal(1,await sexto.Handle(new(),default));Assert.True(f.EnRevision);
            }
            async Task<HttpResponseMessage> Registrar(Guid uuid,decimal importe,string rfc,decimal anterior,decimal insoluto) {
                var xml=$"<cfdi:Comprobante xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\" xmlns:p=\"http://www.sat.gob.mx/Pagos20\" xmlns:t=\"http://www.sat.gob.mx/TimbreFiscalDigital\" Version=\"4.0\" TipoDeComprobante=\"P\" Fecha=\"2026-10-09T12:00:00\"><cfdi:Emisor Rfc=\"{rfc}\"/><cfdi:Complemento><t:TimbreFiscalDigital UUID=\"{uuid}\"/><p:Pagos Version=\"2.0\"><p:Pago FechaPago=\"2026-09-11T12:00:00\" MonedaP=\"MXN\" Monto=\"{importe}\"><p:DoctoRelacionado IdDocumento=\"{uuidFactura}\" MonedaDR=\"MXN\" NumParcialidad=\"1\" ImpSaldoAnt=\"{anterior}\" ImpPagado=\"{importe}\" ImpSaldoInsoluto=\"{insoluto}\"/></p:Pago></p:Pagos></cfdi:Complemento></cfdi:Comprobante>";
                return await client.PostAsJsonAsync("/api/v1/tesoreria/repp-recibidos",new {facturaProveedorId=facturaId,uuidComplemento=uuid,fechaComplemento="2026-10-09",xmlBase64=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(xml)),pagos=new[]{new {pagoId,importe}}});
            }
            var mal=await Registrar(Guid.NewGuid(),20000,"OTR010101ABC",50000,30000);Assert.Equal(HttpStatusCode.UnprocessableEntity,mal.StatusCode);Assert.Equal("REPP_XML_NO_COINCIDE",await Code(mal));
            foreach(var importe in new[]{20000m,30000m}) {
                var uuid=Guid.NewGuid();var bien=await Registrar(uuid,importe,"FIX010101ABC",importe==20000 ? 50000:30000,importe==20000 ? 30000:0);Assert.Equal(HttpStatusCode.Created,bien.StatusCode);
                using var scope=app.Services.CreateScope();await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Millet.CuentasPorPagar.Application.EventListeners.Tesoreria.ReppProveedorRecibidoCommand(Guid.NewGuid(),new(EmpresaBootstrapId,p.Ahora,facturaId,uuid.ToString(),p.Ahora,[new(pagoId,importe)])));
                var f=await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.FindAsync(facturaId);Assert.Equal(importe==20000,f!.EnRevision);Assert.Equal(importe==30000,f.ReppRecibido);
                var pendientes=await Json(await client.GetAsync($"/api/v1/tesoreria/repp-pendientes?proveedorId={p.Proveedor}"));
                if(importe==20000) Assert.Equal(30000,pendientes.GetProperty("items")[0].GetProperty("importePendiente").GetDecimal());else Assert.Equal(0,pendientes.GetProperty("items").GetArrayLength());
            }
        } finally {
            using(var scope=app.Services.CreateScope()) {var tes=scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();await tes.ReppsProveedorRecibidos.Where(r=>r.FacturaProveedorId==facturaId).ExecuteDeleteAsync();await tes.AplicacionesPagoProveedor.Where(a=>a.Id==pagoId).ExecuteDeleteAsync();await tes.MovimientosBancarios.Where(m=>m.Id==movimientoId).ExecuteDeleteAsync();await tes.PasivosPendientesPago.Where(x=>x.FacturaProveedorId==facturaId).ExecuteDeleteAsync();await tes.CuentasBancarias.Where(x=>x.Id==cuentaId).ExecuteDeleteAsync();}
            await Limpiar(app,p);
        }
    }
    private sealed class Reloj(DateOnly fecha):IClock {public DateTimeOffset UtcNow=>new(fecha.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero);}
    private sealed class Blob:Millet.Tesoreria.Domain.Ports.IReppXmlBlobStorage {
        public Task<string> GuardarXmlAsync(string uuid,DateOnly fechaComplemento,Stream contenido,CancellationToken cancellationToken)=>Task.FromResult("FIX-P4/"+uuid+".xml");
        public Task<Stream?> LeerXmlAsync(string blobRef,CancellationToken cancellationToken)=>Task.FromResult<Stream?>(null);
    }
    private static async Task<HttpResponseMessage> Post(HttpClient c,string path,object body,int version) {using var req=new HttpRequestMessage(HttpMethod.Post,Cxp+path){Content=JsonContent.Create(body)};req.Headers.Add("X-Expected-Version",version.ToString(System.Globalization.CultureInfo.InvariantCulture));return await c.SendAsync(req);}
    private static async Task<int> Version(WebApplicationFactory<Program> app,Guid id,bool cargo=false){using var s=app.Services.CreateScope();var db=s.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();return cargo ? (await db.NotasCargo.FindAsync(id))!.Version:(await db.FacturasProveedor.FindAsync(id))!.Version;}
    private static async Task<int> VersionNc(WebApplicationFactory<Program> app,Guid id){using var s=app.Services.CreateScope();return (await s.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().NotasCreditoProveedor.FindAsync(id))!.Version;}
    private static async Task Limpiar(WebApplicationFactory<Program> app,Puertos p) {
        using var s=app.Services.CreateScope();var db=s.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        await db.PagosProveedorLocal.Where(n=>db.FacturasProveedor.Where(f=>f.ProveedorId==p.Proveedor).Select(f=>f.Id).Contains(n.FacturaProveedorId)).ExecuteDeleteAsync();
        await db.NotasCargo.Where(n=>n.ProveedorId==p.Proveedor).ExecuteDeleteAsync();await db.NotasCreditoProveedor.Where(n=>n.ProveedorId==p.Proveedor).ExecuteDeleteAsync();await db.AnticiposProveedor.Where(n=>n.ProveedorId==p.Proveedor).ExecuteDeleteAsync();await db.FacturasProveedor.Where(n=>n.ProveedorId==p.Proveedor).ExecuteDeleteAsync();
        await db.ConfiguracionesAnticipoProveedor.Where(n=>n.ProveedorId==p.Proveedor).ExecuteDeleteAsync();
        var eventos=await db.OutboxEntries.ToListAsync();db.OutboxEntries.RemoveRange(eventos.Where(e=>e.Payload.Contains(p.Proveedor.ToString(),StringComparison.Ordinal)));await db.SaveChangesAsync();Sobre.Value=null;
    }
    private sealed class Puertos:Millet.Tesoreria.Domain.Ports.DatosMaestros.IProveedorBancoReadPort,IProveedorReadPort,IPeriodoContablePort,IDependenciaRevisoraReadPort,Millet.Tesoreria.Domain.Ports.IPeriodoContablePort
    {
        public Guid Proveedor{get;}=Guid.NewGuid();public Guid Sucursal{get;}=Guid.NewGuid();public Guid Dependencia{get;}=Guid.NewGuid();public DateTimeOffset Ahora{get;}=new(2026,10,9,12,0,0,TimeSpan.Zero);
        public Factura NuevaFactura()=>Factura.CapturarSinOc(EmpresaBootstrapId,null,Guid.NewGuid().ToString(),Proveedor,Sucursal,"FIX-P4",null,Ahora,Ahora,new(2026,11,1),"MXN",null,50000,0,0,0,50000,"FIX-P4",Ahora);
        public Anticipo NuevaAnticipo()=>Anticipo.Capturar(EmpresaBootstrapId,null,Guid.NewGuid().ToString(),Proveedor,"FANT","FIX-P4",Ahora,"MXN",null,100,null,null,Ahora);
        public Cargo NuevaCargo(Guid f,decimal monto)=>Cargo.Crear(EmpresaBootstrapId,FolioInternoNotaCargo.FromAnioSecuencial(2026,Random.Shared.Next(800000,999999)),Proveedor,Sucursal,"FIX-P4",null,monto,"MXN",null,f,null,null,Ahora);
        public Nc NuevaNc(Guid? f,decimal total,TipoRelacionCfdi relacion,string uuid)=>Nc.Capturar(EmpresaBootstrapId,null,Guid.NewGuid().ToString(),Proveedor,"FIX-P4",null,Ahora,"MXN",null,total,0,0,total,relacion==TipoRelacionCfdi.Devolucion ? TipoNotaCredito.Devolucion:relacion==TipoRelacionCfdi.AmortizacionAnticipo ? TipoNotaCredito.AmortizacionAnticipo:TipoNotaCredito.Descuento,relacion,uuid,f,null,Ahora);
        Task<Millet.Tesoreria.Domain.Ports.DatosMaestros.ProveedorBancoDto?> Millet.Tesoreria.Domain.Ports.DatosMaestros.IProveedorBancoReadPort.ObtenerAsync(Guid id,CancellationToken cancellationToken)=>Task.FromResult<Millet.Tesoreria.Domain.Ports.DatosMaestros.ProveedorBancoDto?>(new(id,"FIX-P4","FIX proveedor",null,null,null,Rfc:"FIX010101ABC"));
        public Task<IReadOnlyDictionary<Guid,Millet.Tesoreria.Domain.Ports.DatosMaestros.ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids,CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyDictionary<Guid,Millet.Tesoreria.Domain.Ports.DatosMaestros.ProveedorBancoDto>>(ids.ToDictionary(id=>id,id=>new Millet.Tesoreria.Domain.Ports.DatosMaestros.ProveedorBancoDto(id,"FIX-P4","FIX proveedor",null,null,null,Rfc:"FIX010101ABC")));
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha,CancellationToken ct)=>Task.FromResult(true);
        public Task<bool> EstaAbiertoAsync(int año,int mes,CancellationToken ct)=>Task.FromResult(true);
        public Task<ProveedorDto?> ObtenerAsync(Guid id,CancellationToken ct)=>Task.FromResult<ProveedorDto?>(new(id,"FIX010101ABC","FIX Proveedor P4",null,false,true));
        public Task<ProveedorDto?> ObtenerPorRfcAsync(string r,CancellationToken ct)=>ObtenerAsync(Proveedor,ct);
        public Task<IReadOnlyDictionary<Guid,string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids,CancellationToken ct)=>Task.FromResult<IReadOnlyDictionary<Guid,string>>(ids.ToDictionary(id=>id,_=>"FIX Proveedor P4"));
        Task<DependenciaRevisoraDto?> IDependenciaRevisoraReadPort.ObtenerAsync(Guid id,CancellationToken ct)=>Task.FromResult<DependenciaRevisoraDto?>(new(id,"CXP","FIX CxP",true));
        public Task<IReadOnlyList<DependenciaRevisoraDto>> ListarAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<DependenciaRevisoraDto>>([new(Dependencia,"CXP","FIX CxP",true)]);
    }
}
