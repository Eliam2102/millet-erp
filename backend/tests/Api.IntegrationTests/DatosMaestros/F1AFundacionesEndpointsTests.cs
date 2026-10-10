using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
namespace Millet.Api.IntegrationTests.DatosMaestros;

public sealed class F1AFundacionesEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Clasificacion = "00000012-0001-0000-0000-000000000001";
    private async Task<HttpClient> ClienteAsync()
    {
        var c=factory.CreateClientWithIdempotency();
        var login=await c.PostAsJsonAsync("/api/dev/fake-login",new { EntraOid="dev-superadmin",Email="superadmin@dev.local",Nombre="SuperAdmin",EmpresaId=Guid.Parse("00000003-0000-0000-0000-000000000001") });
        var j=await Json(login); c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",j.GetProperty("accessToken").GetString()); return c;
    }
    private static async Task<JsonElement> Json(HttpResponseMessage r)
    { r.EnsureSuccessStatusCode(); using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.Clone(); }
    private static object Alta(string clave,string rfc) => new {
        clave,razonSocial="CLIENTE FICTICIO F1A",rfc,regimenFiscal="601",codigoPostalFiscal="97000",usoCfdiDefault="G03",
        formaPagoDefault="03",metodoPagoDefault="PUE",monedaDefault="MXN",clasificacionId=Clasificacion };
    private async Task LimpiarAsync(string referencia,IReadOnlyList<Guid> clientes,IReadOnlyList<Guid>? productos=null,IReadOnlyList<Guid>? reglas=null)
    {
        using var scope=factory.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        db.RemoveRange(await db.Set<ClienteLigadoAw>().Where(c=>c.ReferenciaExterna==referencia).ToListAsync());
        db.RemoveRange(await db.ClientesSincronizacionAw.Where(c=>clientes.Contains(c.ClienteId)).ToListAsync()); await db.SaveChangesAsync();
        db.RemoveRange(await db.Clientes.Where(c=>clientes.Contains(c.Id)).ToListAsync());
        if(productos is not null)
        {
            db.RemoveRange(await db.ProductosSincronizacionAw.Where(p=>productos.Contains(p.ProductoAwId)).ToListAsync());
            db.RemoveRange(await db.Set<CambioFiscalAw>().Where(c=>productos.Contains(c.EntidadId)).ToListAsync()); await db.SaveChangesAsync();
            db.RemoveRange(await db.ProductosAw.Where(p=>productos.Contains(p.Id)).ToListAsync());
        }
        await db.SaveChangesAsync();
        if(reglas is not null)
        {
            db.RemoveRange(await db.Set<CambioFiscalAw>().Where(c=>reglas.Contains(c.EntidadId)).ToListAsync());
            db.RemoveRange(await db.Set<ReglaFiscalAw>().Where(r=>reglas.Contains(r.Id)).ToListAsync()); await db.SaveChangesAsync();
        }
    }
    [Fact]
    public async Task Alta_manual_completa_duplicado_ofrece_existente_y_ligado_revisado_conserva_id_y_clasificacion()
    {
        using var c=await ClienteAsync(); var referencia="F1A-"+Guid.NewGuid().ToString("N")[..8]; var ids=new List<Guid>();
        // RFC ficticio distinto por prueba, con formato SAT de persona moral.
        var rfc="FIA261010"+Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
        try
        {
            var alta=await Json(await c.PostAsJsonAsync("/api/v1/datos-maestros/clientes",Alta(referencia,rfc))); var id=alta.GetProperty("id").GetGuid(); ids.Add(id);
            var duplicado=await c.PostAsJsonAsync("/api/v1/datos-maestros/clientes",Alta(referencia+"D",rfc)); Assert.Equal(HttpStatusCode.UnprocessableEntity,duplicado.StatusCode);
            using(var j=JsonDocument.Parse(await duplicado.Content.ReadAsStringAsync())) Assert.Equal(id,j.RootElement.GetProperty("clientesExistentes")[0].GetProperty("id").GetGuid());
            using(var scope=factory.Services.CreateScope())
            {
                var svc=scope.ServiceProvider.GetRequiredService<AplicarClienteAwService>();
                var r=await svc.AplicarAsync(new(referencia,"NOMBRE DE A+W",DateTime.UtcNow,"1","F1A",Rfc:rfc,MonedaDefault:"MXN"),CancellationToken.None);
                Assert.Equal(AplicarClienteAwAccion.Conflicto,r.Accion);
            }
            var conflictos=await Json(await c.GetAsync("/api/v1/datos-maestros/clientes/ligados-aw")); var conflicto=conflictos.EnumerateArray().Single(x=>x.GetProperty("referenciaExterna").GetString()==referencia);
            var conflictoId=conflicto.GetProperty("id").GetGuid();
            var revision=await c.PostAsJsonAsync($"/api/v1/datos-maestros/clientes/ligados-aw/{conflictoId}/revisar",new { id=conflictoId,clienteId=id,version=conflicto.GetProperty("version").GetInt32() }); revision.EnsureSuccessStatusCode();
            var detalle=await Json(await c.GetAsync($"/api/v1/datos-maestros/clientes/{id}"));
            Assert.Equal(referencia,detalle.GetProperty("referenciaExterna").GetString()); Assert.Equal(Clasificacion,detalle.GetProperty("clasificacionId").GetString()); Assert.Equal("CLIENTE FICTICIO F1A",detalle.GetProperty("razonSocial").GetString());
            Assert.Equal(1,(await Json(await c.GetAsync($"/api/v1/datos-maestros/clientes?rfc={rfc}"))).GetProperty("total").GetInt32());
            var falta=await c.PatchAsJsonAsync($"/api/v1/datos-maestros/clientes/{id}",new { telefono="9990000000" }); Assert.Equal(HttpStatusCode.BadRequest,falta.StatusCode);
        }
        finally { await LimpiarAsync(referencia,ids); }
    }
    [Fact]
    public async Task Clasificacion_CRUD_baja_logica_y_filtro_sin_clasificar()
    {
        using var c=await ClienteAsync(); var referencia="F1A-"+Guid.NewGuid().ToString("N")[..8]; var clientes=new List<Guid>(); Guid? clasificacionId=null;
        try
        {
            var alta=await Json(await c.PostAsJsonAsync("/api/v1/datos-maestros/clasificaciones-cliente",new { nombre=referencia,descripcion="FICTICIO",activa=true })); clasificacionId=alta.GetProperty("id").GetGuid();
            var listado=await Json(await c.GetAsync("/api/v1/datos-maestros/clasificaciones-cliente")); var item=listado.EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==clasificacionId);
            (await c.PutAsJsonAsync($"/api/v1/datos-maestros/clasificaciones-cliente/{clasificacionId}",new { nombre=referencia,descripcion="FICTICIO editado",activa=false,version=item.GetProperty("version").GetInt32() })).EnsureSuccessStatusCode();
            using var scope=factory.Services.CreateScope(); var servicio=scope.ServiceProvider.GetRequiredService<AplicarClienteAwService>(); var r=await servicio.AplicarAsync(new(referencia,"FICTICIO",DateTime.UtcNow,"1","F1A",MonedaDefault:"MXN"),CancellationToken.None); clientes.Add(r.Cliente!.Id);
            var sin=await Json(await c.GetAsync($"/api/v1/datos-maestros/clientes?sinClasificar=true&referenciaExterna={referencia}")); Assert.Equal(1,sin.GetProperty("total").GetInt32());
        }
        finally
        {
            await LimpiarAsync(referencia,clientes);
            if(clasificacionId is {} id) { using var scope=factory.Services.CreateScope(); var db=scope.ServiceProvider.GetRequiredService<CompartidoDbContext>(); db.Remove(await db.Set<ClasificacionCliente>().SingleAsync(c=>c.Id==id)); await db.SaveChangesAsync(); }
        }
    }
    [Fact]
    public async Task CSV_dos_importaciones_sin_duplicados_errores_por_fila_y_excepcion_no_se_pisa()
    {
        using var c=await ClienteAsync(); var valor="F1A-DEMO-"+Guid.NewGuid().ToString("N")[..8]; var productos=new List<Guid>(); var reglas=new List<Guid>();
        try
        {
            using(var scope=factory.Services.CreateScope())
            {
                var s=scope.ServiceProvider.GetRequiredService<AplicarProductoAwService>(); var r=await s.AplicarAsync(new(valor,"PRODUCTO FICTICIO F1A","PZA",false,[],DateTime.UtcNow,"1","F1A",SoloCrear:true,Tipo:valor),CancellationToken.None); productos.Add(r.Producto!.Id);
            }
            var csv="clase,campo,valor,descripcion,claveProdServSat,claveUnidadSat,objetoImp,tasaIvaTraslado,tasaRetencionIva,tasaRetencionIsr\n"+
                $"Regla,Tipo,{valor},DEMO,30171706,H87,02,0.16,,\nExcepcion,,{valor},DEMO,30171708,H87,02,0,,\nRegla,Tipo,{valor}-ERROR,DEMO,incorrecta,H87,02,0.16,,";
            var primero=await Json(await c.PostAsJsonAsync("/api/v1/datos-maestros/productos-aw/fiscal/importar",new { csv })); Assert.Equal(2,primero.GetProperty("aplicadas").GetInt32()); Assert.Equal(4,primero.GetProperty("errores")[0].GetProperty("fila").GetInt32());
            var listado=await Json(await c.GetAsync("/api/v1/datos-maestros/productos-aw/fiscal/reglas")); var regla=listado.EnumerateArray().Single(r=>r.GetProperty("valor").GetString()==valor); reglas.Add(regla.GetProperty("id").GetGuid());
            var segundo=await Json(await c.PostAsJsonAsync("/api/v1/datos-maestros/productos-aw/fiscal/importar",new { csv })); Assert.Equal(0,segundo.GetProperty("aplicadas").GetInt32()); Assert.Equal(2,segundo.GetProperty("sinCambios").GetInt32());
            var producto=await Json(await c.GetAsync($"/api/v1/datos-maestros/productos-aw/{productos[0]}"));
            (await c.PostAsJsonAsync($"/api/v1/datos-maestros/productos-aw/fiscal/{productos[0]}/acciones",new { accion="Confirmar",version=producto.GetProperty("version").GetInt32() })).EnsureSuccessStatusCode();
            var confirmada=await Json(await c.GetAsync($"/api/v1/datos-maestros/productos-aw/{productos[0]}")); Assert.NotEqual(JsonValueKind.Null,confirmada.GetProperty("confirmadoPor").ValueKind);
            (await c.PutAsJsonAsync($"/api/v1/datos-maestros/productos-aw/fiscal/reglas/{reglas[0]}",new { campo="Tipo",valor,descripcion="DEMO cambiado",activa=true,version=regla.GetProperty("version").GetInt32(),ficha=new { claveProdServSat="30171710",claveUnidadSat="H87",objetoImp="02",tasaIvaTraslado=0.16m } })).EnsureSuccessStatusCode();
            Assert.Equal("30171708",(await Json(await c.GetAsync($"/api/v1/datos-maestros/productos-aw/{productos[0]}"))).GetProperty("claveProdServSat").GetString());
            var historial=await Json(await c.GetAsync($"/api/v1/datos-maestros/productos-aw/fiscal/Producto/{productos[0]}/historial")); Assert.True(historial.GetArrayLength()>=2);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var creadas = await db.Set<ReglaFiscalAw>().Where(r => r.Valor == valor).Select(r => r.Id).ToListAsync();
            await LimpiarAsync(valor, [], productos, creadas);
        }
    }
    [Fact]
    public async Task Endpoints_nuevos_exigen_sesion()
    {
        using var c=factory.CreateClient();
        foreach(var path in new[]{"/api/v1/datos-maestros/clasificaciones-cliente","/api/v1/datos-maestros/clientes/ligados-aw","/api/v1/datos-maestros/productos-aw/fiscal/reglas"})
            Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(path)).StatusCode);
    }
}
