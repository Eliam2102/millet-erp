using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
public sealed partial class LibroHttpTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task P17_corregir_ultimo_traslado_liga_historial_y_rechaza_mes_confirmado(bool confirmado)
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var referencia=await Preparar(e,false);Guid id,traslado;
        using(var scope=e.Scope())
        {
            var sp=scope.ServiceProvider;using var bypass=sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=sp.GetRequiredService<ActivosFijosDbContext>();var a=await db.Activos.SingleAsync(x=>x.Id==referencia);
            var ahora=sp.GetRequiredService<IClock>().UtcNow;var hoy=sp.GetRequiredService<IClock>().HoyLocal();
            var nuevo=new Activo(Guid.NewGuid(),a.EmpresaId,"P17-TRASLADO",new("Equipo P17",a.ClaseId,null,null,null,hoy.AddDays(-1),hoy.AddDays(-1),1000,"MXN",1,a.Custodia(),TipoImportacionActivo.No,null,false,"Factura"));nuevo.Capitalizar(Guid.NewGuid());
            traslado=Guid.NewGuid();nuevo.Trasladar(traslado,nuevo.Custodia() with {Ubicacion="Destino equivocado"},"Traslado original",hoy,e.Responsable,ahora);db.Add(nuevo);id=nuevo.Id;
            if(confirmado) db.Add(new CorridaDepreciacion {EmpresaId=a.EmpresaId,Periodo=CalculoDepreciacion.Mes(hoy),Revision=1});await db.SaveChangesAsync();
        }
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);var actual=await cliente.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");
        using var corregir=await Post(cliente,$"activos/{id}/corregir-traslado",new {trasladoId=traslado,destino=new {responsableId=e.Responsable,sucursalId=e.SucursalId,ubicacion="Destino corregido",centroCostoId=actual.GetProperty("centroCostoId").GetGuid(),equipoId=(Guid?)null},motivo="Error de ubicación"},actual.GetProperty("version").GetInt32());
        Assert.Equal(confirmado ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK,corregir.StatusCode);
        if(!confirmado) {actual=await cliente.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");Assert.Equal("Destino corregido",actual.GetProperty("ubicacion").GetString());var historial=actual.GetProperty("traslados").EnumerateArray().ToList();Assert.True(historial.Single(t=>t.GetProperty("id").GetGuid()==traslado).GetProperty("corregido").GetBoolean());Assert.Contains(historial,t=>t.GetProperty("corrigeA").ValueKind!=JsonValueKind.Null&&t.GetProperty("corrigeA").GetGuid()==traslado);}
    }
}
