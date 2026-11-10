using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Millet.Api.Auth;
using Millet.Api.Seed;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
// Comparte Collection(ApiCompartida.Nombre) declarada en LibroHttpTests y su preparación contable.
public sealed partial class LibroHttpTests
{
    [Fact] public async Task P17_baja_duplicada_rechazo_motivado_y_venta_abierta_se_excluyen()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);
        using var captura=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using var daf=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var a=await captura.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");var version=a.GetProperty("version").GetInt32();
        var datos=new { tipo="Baja",fecha="2027-02-01",importe=0,motivo="Siniestro",solicitadoPor="Área usuaria",evidencia="data:application/pdf;base64," + Convert.ToBase64String(Millet.Api.Seed.DemoSesionSeedHostedService.CrearPdf("Evidencia QA sintética")) };
        using var primero=await Post(captura,$"activos/{id}/operaciones",datos,version);primero.EnsureSuccessStatusCode();var op=(await primero.Content.ReadFromJsonAsync<JsonElement>());
        using(var ventaClient=await e.ClienteAsync(true,facturacion:true,permisosCe:false))
        using(var ventaReq=new HttpRequestMessage(HttpMethod.Post,"/api/v1/facturacion/activos/autorizar") { Content=JsonContent.Create(new {activoRef="FIX-P4-MONTACARGAS",precioVenta=100,motivo=5,solicitadoPor="Área usuaria",evidencia="Factura"}) })
        {
            ventaReq.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());using var ventaRespuesta=await ventaClient.SendAsync(ventaReq);Assert.Equal(HttpStatusCode.UnprocessableEntity,ventaRespuesta.StatusCode);
            Assert.Contains("ACTIVO_BAJA_PENDIENTE", await ventaRespuesta.Content.ReadAsStringAsync());
        }
        using var duplicada=await Post(captura,$"activos/{id}/operaciones",datos,version);Assert.Equal(HttpStatusCode.UnprocessableEntity,duplicada.StatusCode);
        using var vacio=await Post(daf,$"operaciones/{op.GetProperty("id").GetGuid()}/rechazar",new {motivo=" "},op.GetProperty("version").GetInt32());Assert.Equal(HttpStatusCode.UnprocessableEntity,vacio.StatusCode);
        using var rechazo=await Post(daf,$"operaciones/{op.GetProperty("id").GetGuid()}/rechazar",new {motivo="No procede el siniestro"},op.GetProperty("version").GetInt32());rechazo.EnsureSuccessStatusCode();
        using(var scope=e.Scope())
        {
            using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var af=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();Assert.Equal("No procede el siniestro",(await af.Set<OperacionLibro>().SingleAsync()).MotivoRechazo);
            var activo=await af.Activos.SingleAsync(x=>x.Id==id);
            var fact=scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();fact.AutorizacionesVentaActivo.Add(AutorizacionVentaActivo.Crear(activo.EmpresaId,activo.Numero,activo.Descripcion,activo.ValorActual,0,false,100,Guid.NewGuid(),DateTimeOffset.UtcNow,MotivoDisposicionActivo.Venta,"Área usuaria","Soporte"));await fact.SaveChangesAsync();
        }
        using var ventaAbierta=await Post(captura,$"activos/{id}/operaciones",datos,version);Assert.Equal(HttpStatusCode.UnprocessableEntity,ventaAbierta.StatusCode);
        Assert.Contains("Tiene una autorización de venta vigente",(await ventaAbierta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString());
    }
    [Fact] public async Task P17_reversa_baja_autorizada_reintegra_y_siguiente_corrida_recupera_meses()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);
        using var contador=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);using var daf=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var a=await contador.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");
        using var solicitar=await Post(contador,$"activos/{id}/operaciones",new {tipo="Baja",fecha="2027-02-01",importe=0,motivo="Siniestro",solicitadoPor="Área usuaria",evidencia="data:application/pdf;base64," + Convert.ToBase64String(Millet.Api.Seed.DemoSesionSeedHostedService.CrearPdf("Evidencia QA sintética"))},a.GetProperty("version").GetInt32());solicitar.EnsureSuccessStatusCode();var op=await solicitar.Content.ReadFromJsonAsync<JsonElement>();
        using var autorizar=await Post(daf,$"operaciones/{op.GetProperty("id").GetGuid()}/autorizar",new {},op.GetProperty("version").GetInt32());autorizar.EnsureSuccessStatusCode();
        foreach(var mes in new[] {"2027-02-01","2027-03-01"}) {using var corrida=await Post(contador,"depreciacion",new {periodo=mes});corrida.EnsureSuccessStatusCode();}
        a=await contador.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");
        using var reversa=await Post(contador,$"activos/{id}/operaciones",new {tipo="ReversaBaja",fecha="2027-04-01",importe=0,motivo="Dictamen corregido",revierteA=op.GetProperty("id").GetGuid()},a.GetProperty("version").GetInt32());reversa.EnsureSuccessStatusCode();var r=await reversa.Content.ReadFromJsonAsync<JsonElement>();
        using var misma=await Post(contador,$"operaciones/{r.GetProperty("id").GetGuid()}/autorizar",new {},r.GetProperty("version").GetInt32());Assert.Equal(HttpStatusCode.UnprocessableEntity,misma.StatusCode);
        using var autorizada=await Post(daf,$"operaciones/{r.GetProperty("id").GetGuid()}/autorizar",new {},r.GetProperty("version").GetInt32());autorizada.EnsureSuccessStatusCode();
        using var abril=await Post(contador,"depreciacion",new {periodo="2027-04-01"});abril.EnsureSuccessStatusCode();Assert.Equal(12500.01m,(await abril.Content.ReadFromJsonAsync<CorridaDepreciacion>())!.Detalles.Single(d=>d.ActivoId==id).Importe);
        using var mayo=await Post(contador,"depreciacion",new {periodo="2027-05-01"});mayo.EnsureSuccessStatusCode();Assert.Equal(4166.67m,(await mayo.Content.ReadFromJsonAsync<CorridaDepreciacion>())!.Detalles.Single(d=>d.ActivoId==id).Importe);
    }
    [Fact] public async Task P17_venta_cancelada_dos_meses_espera_autorizacion_y_recupera_tres_meses()
    {
        var venta = new VentaFixture { Fecha = new(2027,3,1) };
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(s => s.Replace(ServiceDescriptor.Singleton<IVentaActivoReadPort>(venta)));
        var id = await Preparar(e,false,0); var factura=Guid.NewGuid();
        async Task Consumir()
        {
            using var scope=e.Scope(); var sp=scope.ServiceProvider; var accessor=sp.GetRequiredService<IHttpContextAccessor>();var previo=accessor.HttpContext;
            try { accessor.HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(MilletClaimTypes.CurrentEmpresaId,Millet.Api.Seed.DemoSesionSeedHostedService.EmpresaId.ToString())],"P17")) }; await sp.GetRequiredService<VentaActivoConsumer>().ProcesarAsync(factura,default); }
            finally { accessor.HttpContext=previo; }
        }
        await Consumir();
        using var contador=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);using var daf=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        foreach(var mes in new[] {"2027-03-01","2027-04-01"}) {using var corrida=await Post(contador,"depreciacion",new {periodo=mes});corrida.EnsureSuccessStatusCode();}
        venta.Cancelada=true;await Consumir();await Consumir();
        var a=await contador.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");Assert.Equal(3,a.GetProperty("estado").GetInt32());
        Guid origen;
        using(var scope=e.Scope()) {using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();var op=await db.Set<OperacionLibro>().SingleAsync();Assert.False(op.Anulada);Assert.True(op.VentaCancelada);origen=op.Id;}
        using var reversa=await Post(contador,$"activos/{id}/operaciones",new {tipo="ReversaVenta",fecha="2027-05-01",importe=0,motivo="CFDI cancelado",revierteA=origen},a.GetProperty("version").GetInt32());reversa.EnsureSuccessStatusCode();var r=await reversa.Content.ReadFromJsonAsync<JsonElement>();
        using var autorizar=await Post(daf,$"operaciones/{r.GetProperty("id").GetGuid()}/autorizar",new {},r.GetProperty("version").GetInt32());autorizar.EnsureSuccessStatusCode();
        using var mayo=await Post(contador,"depreciacion",new {periodo="2027-05-01"});mayo.EnsureSuccessStatusCode();Assert.Equal(12500.01m,(await mayo.Content.ReadFromJsonAsync<CorridaDepreciacion>())!.Detalles.Single(d=>d.ActivoId==id).Importe);
    }
    [Fact] public async Task P17_impresion_reposicion_y_recaptura_con_historial_persistido()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var a=await cliente.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");
        using var primera=await Post(cliente,"etiquetas/registrar-impresion",new {activos=new[] {new {id,version=a.GetProperty("version").GetInt32()}}});primera.EnsureSuccessStatusCode();
        a=await cliente.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");var items=new[] {new {id,version=a.GetProperty("version").GetInt32()}};
        using var sinMotivo=await Post(cliente,"etiquetas/registrar-impresion",new {activos=items});Assert.Equal(HttpStatusCode.UnprocessableEntity,sinMotivo.StatusCode);
        using var reposicion=await Post(cliente,"etiquetas/registrar-impresion",new {activos=items,motivo="Pérdida"});reposicion.EnsureSuccessStatusCode();
        a=await cliente.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");Assert.Equal(2,a.GetProperty("impresiones").GetArrayLength());
        using var abrir=await Post(cliente,"conteos",new {sucursalId=e.SucursalId});abrir.EnsureSuccessStatusCode();var c=await abrir.Content.ReadFromJsonAsync<JsonElement>();var cid=c.GetProperty("id").GetGuid();
        using var captura=await Post(cliente,$"conteos/{cid}/capturar",new {captura=new {numero="FIX-P4-MONTACARGAS",resultado=1,evidencia="Foto anterior"}},c.GetProperty("version").GetInt32());captura.EnsureSuccessStatusCode();c=await captura.Content.ReadFromJsonAsync<JsonElement>();
        using var sinRazon=await Post(cliente,$"conteos/{cid}/capturar",new {captura=new {numero="FIX-P4-MONTACARGAS",resultado=2,evidencia="Foto nueva"}},c.GetProperty("version").GetInt32());Assert.Equal(HttpStatusCode.UnprocessableEntity,sinRazon.StatusCode);
        using var recaptura=await Post(cliente,$"conteos/{cid}/capturar",new {captura=new {numero="FIX-P4-MONTACARGAS",resultado=2,evidencia="Foto nueva",motivo="Error de lectura"}},c.GetProperty("version").GetInt32());recaptura.EnsureSuccessStatusCode();
        c=await cliente.GetFromJsonAsync<JsonElement>($"{Base}/conteos/{cid}");Assert.Equal("Foto anterior",Assert.Single(c.GetProperty("partidas").EnumerateArray(),p=>p.GetProperty("activoId").GetGuid()==id).GetProperty("capturasAnteriores")[0].GetProperty("evidencia").GetString());
    }
}
