using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
public sealed partial class LibroHttpTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task P17_capitalizacion_reversa_autorizada_poliza_inversa_o_rechazo_por_depreciacion(bool depreciar)
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var referencia=await Preparar(e,false);Guid id;
        using(var scope=e.Scope())
        {
            using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();var a=await db.Activos.SingleAsync(x=>x.Id==referencia);
            var nuevo=new Activo(Guid.NewGuid(),a.EmpresaId,"P17-CAP",new("Equipo P17",a.ClaseId,null,null,null,a.FechaAdquisicion,a.InicioUso,a.ValorOriginal,"MXN",1,a.Custodia(),TipoImportacionActivo.No,null,false,"Factura P17"));db.Add(nuevo);await db.SaveChangesAsync();id=nuevo.Id;
        }
        using var contador=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);using var daf=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var aJson=await contador.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");
        using var capitalizar=await Post(contador,$"activos/{id}/capitalizar",new {},aJson.GetProperty("version").GetInt32());capitalizar.EnsureSuccessStatusCode();aJson=await capitalizar.Content.ReadFromJsonAsync<JsonElement>();var polizaId=aJson.GetProperty("polizaAltaId").GetGuid();
        if(depreciar) {using var corrida=await Post(contador,"depreciacion",new {periodo="2027-02-01"});corrida.EnsureSuccessStatusCode();aJson=await contador.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");}
        using var solicitar=await Post(contador,$"activos/{id}/operaciones",new {tipo="ReversaCapitalizacion",fecha="2027-03-01",importe=0,motivo="Corregir capitalización"},aJson.GetProperty("version").GetInt32());
        if(depreciar) {Assert.Equal(HttpStatusCode.UnprocessableEntity,solicitar.StatusCode);Assert.Contains("depreciación",await solicitar.Content.ReadAsStringAsync());return;}
        solicitar.EnsureSuccessStatusCode();var op=await solicitar.Content.ReadFromJsonAsync<JsonElement>();
        using var misma=await Post(contador,$"operaciones/{op.GetProperty("id").GetGuid()}/autorizar",new {},op.GetProperty("version").GetInt32());Assert.Equal(HttpStatusCode.UnprocessableEntity,misma.StatusCode);
        using var autorizar=await Post(daf,$"operaciones/{op.GetProperty("id").GetGuid()}/autorizar",new {},op.GetProperty("version").GetInt32());autorizar.EnsureSuccessStatusCode();
        aJson=await contador.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");Assert.Equal(0,aJson.GetProperty("estado").GetInt32());Assert.Equal(JsonValueKind.Null,aJson.GetProperty("polizaAltaId").ValueKind);
        using var ver=e.Scope();using var lectura=ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var contab=ver.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var original=await contab.Polizas.Include(x=>x.Partidas).SingleAsync(x=>x.Id==polizaId);var inversa=await contab.Polizas.Include(x=>x.Partidas).SingleAsync(x=>x.PolizaAnuladaId==polizaId);
        Assert.Equal(EstadoPoliza.Anulada,original.Estado);Assert.Equal(-original.TotalCargos,inversa.TotalCargos);Assert.Equal(-original.TotalAbonos,inversa.TotalAbonos);
    }
}
