using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.ActivosFijos.Infrastructure.Workers;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
public sealed partial class LibroHttpTests
{
    [Fact] public async Task P17_cerrar_aviso_exige_motivo_y_worker_no_recrea_misma_vigencia()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);Guid fuente;
        using(var scope=e.Scope())
        {
            using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();var a=await db.Activos.SingleAsync(x=>x.Id==id);
            var d=new DocumentoActivo(a,new(TipoDocumentoActivo.Seguro,Guid.NewGuid(),"P17-POLIZA","Aseguradora",new(2026,1,1),new(2026,1,1),new(2026,10,1),null));db.Add(d);await db.SaveChangesAsync();fuente=d.Id;
        }
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using var vacio=await Post(cliente,$"activos/{id}/avisos/{fuente}/resolver",new {resolucion="Cerrado",motivo=""});Assert.Equal(HttpStatusCode.UnprocessableEntity,vacio.StatusCode);
        using var cerrar=await Post(cliente,$"activos/{id}/avisos/{fuente}/resolver",new {resolucion="Cerrado",motivo="Se retira de cobertura"});cerrar.EnsureSuccessStatusCode();
        using var ver=e.Scope();using var lectura=ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var dbVer=ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        Assert.Equal(0,await AvisosActivosWorker.GenerarAsync(dbVer,new(2027,1,1),new(),default));Assert.Single(await dbVer.Avisos.Where(x=>x.FuenteId==fuente).ToListAsync());
        var lista=await cliente.GetFromJsonAsync<JsonElement>($"{Base}/por-vencer");Assert.DoesNotContain(lista.GetProperty("items").EnumerateArray(),x=>x.GetProperty("id").GetGuid()==fuente);
    }
    [Fact] public async Task P17_renovacion_exige_nueva_vigencia_y_genera_otro_aviso_al_vencer()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);Guid fuente,nueva;
        using(var scope=e.Scope())
        {
            using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();var a=await db.Activos.SingleAsync(x=>x.Id==id);
            var anterior=new DocumentoActivo(a,new(TipoDocumentoActivo.Seguro,Guid.NewGuid(),"Anterior","Seguro",new(2026,1,1),new(2026,1,1),new(2026,10,1),null));db.Add(anterior);
            var actual=new DocumentoActivo(a,new(TipoDocumentoActivo.Seguro,Guid.NewGuid(),"Renovada","Seguro",new(2026,10,1),new(2026,10,1),new(2027,10,1),null,anterior.Id),anterior);db.Add(actual);await db.SaveChangesAsync();fuente=anterior.Id;nueva=actual.Id;
        }
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using var sinDocumento=await Post(cliente,$"activos/{id}/avisos/{fuente}/resolver",new {resolucion="Renovado"});Assert.Equal(HttpStatusCode.UnprocessableEntity,sinDocumento.StatusCode);
        using var renovar=await Post(cliente,$"activos/{id}/avisos/{fuente}/resolver",new {resolucion="Renovado",documentoNuevoId=nueva});renovar.EnsureSuccessStatusCode();
        using var ver=e.Scope();using var lectura=ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var dbVer=ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        Assert.Equal(1,await AvisosActivosWorker.GenerarAsync(dbVer,new(2027,10,2),new(),default));Assert.Equal(0,await AvisosActivosWorker.GenerarAsync(dbVer,new(2027,10,3),new(),default));Assert.NotNull((await dbVer.Avisos.SingleAsync(x=>x.FuenteId==fuente)).ResueltoEn);Assert.Null((await dbVer.Avisos.SingleAsync(x=>x.FuenteId==nueva)).ResueltoEn);
    }

}
