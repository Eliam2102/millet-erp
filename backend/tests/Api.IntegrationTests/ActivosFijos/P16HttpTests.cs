using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
public sealed partial class LibroHttpTests
{
    [Fact] public async Task P16_SAP_real_previa_sin_columnas_clases_inventar_acumulada_y_confirmacion_rechazada()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();await Preparar(e,false);
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using(var scope=e.Scope()) {using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();var c=await db.Clases.SingleAsync(c => c.Clave == "154");c.Editar(c.Nombre,25,c.CuentaActivoId,c.CuentaDepreciacionId,c.CuentaGastoId,c.CuentaPuenteId,true);await db.SaveChangesAsync();}
        using var recurso=typeof(LibroHttpTests).Assembly.GetManifestResourceStream("Millet.Api.IntegrationTests.Recursos.SapMilletP16.xlsx")!;
        using var bytes=new MemoryStream();await recurso.CopyToAsync(bytes);
        foreach (var confirmar in new[] { false,true })
        {
            using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes.ToArray()),"archivo","sap.xlsx");
            form.Add(new StringContent("P16"),"versionArchivo");form.Add(new StringContent("2027-01-31"),"corte");form.Add(new StringContent(e.SucursalId.ToString()),"sucursalId");
            using var req=new HttpRequestMessage(HttpMethod.Post,$"{Base}/carga/{(confirmar ? "confirmar" : "previa")}") {Content=form};req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());
            using var r=await cliente.SendAsync(req);r.EnsureSuccessStatusCode();var resultado=(await r.Content.ReadFromJsonAsync<ResultadoCarga>())!;
            Assert.Equal(!confirmar,resultado.Valida);Assert.DoesNotContain(resultado.Errores,x => x.Columna == "clase" || x.Codigo == "AF_COLUMNA_FALTANTE");
            Assert.Contains(resultado.Avisos!,x => x.Codigo == "AF_CARGA_SIN_ACUMULADA");Assert.All(resultado.Cuadre,c => Assert.Null(c.Acumulada));
            var forklift=resultado.Activos!.Single(a => a.Numero == "ACF99575");Assert.Equal("153",forklift.Clase);Assert.Equal(25.0014m,forklift.TasaContablePropia);
            var apc=resultado.Activos!.Single(a => a.Numero == "ACF99620");Assert.Equal("156",apc.Clase);Assert.Null(apc.TasaContablePropia);
            var aire=resultado.Activos!.Single(a => a.Numero == "ACF99215");Assert.Equal("152",aire.Clase);Assert.Null(aire.TasaContablePropia);
            var touareg=resultado.Activos!.Single(a => a.Numero == "ACF98840");Assert.Equal("154",touareg.Clase);Assert.Equal(10,touareg.TasaContablePropia);
            if (confirmar) Assert.Contains(resultado.Errores,x => x.Codigo == "AF_CARGA_SIN_ACUMULADA");
        }
    }
    [Fact] public async Task P16_sin_tasa_no_confirma_y_nombra_el_activo()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);
        using(var scope=e.Scope())
        {
            using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
            var a=await db.Activos.SingleAsync(a => a.Id == id);var c=await db.Clases.SingleAsync(c => c.Id == a.ClaseId);c.Editar(c.Nombre,null,c.CuentaActivoId,c.CuentaDepreciacionId,c.CuentaGastoId,c.CuentaPuenteId,true);await db.SaveChangesAsync();
        }
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using var r=await Post(cliente,"depreciacion",new {periodo="2027-02-01"});Assert.Equal(HttpStatusCode.UnprocessableEntity,r.StatusCode);Assert.Contains("FIX-P4-MONTACARGAS",await r.Content.ReadAsStringAsync());
    }
    [Fact] public async Task P16_poliza_anulada_se_recupera_sin_recalcular_y_no_duplica()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using var r=await Post(cliente,"depreciacion",new {periodo="2027-02-01"});r.EnsureSuccessStatusCode();var corrida=(await r.Content.ReadFromJsonAsync<CorridaP21>())!;
        using(var scope=e.Scope())
        {
            using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<IPolizaContablePort>().AnularPorDocumentoOrigenAsync("ActivosFijos","Depreciacion",corrida.Id,"Prueba P16",default);
        }
        var historial=await cliente.GetFromJsonAsync<List<CorridaP21>>($"{Base}/depreciacion");Assert.True(historial!.Single(c => c.Id == corrida.Id).SinEfectoContable);
        var conciliacion=await cliente.GetFromJsonAsync<ReporteConciliacion>($"{Base}/conciliacion?periodo=2027-02-01");Assert.Equal(4166.67m,conciliacion!.DepreciacionPorActivo!.Single(a => a.ActivoId == id).Diferencia);
        Guid? nueva=null;
        for(var intento=0;intento<2;intento++)
        {
            using var recuperar=await Post(cliente,$"depreciacion/{corrida.Id}/recuperar-poliza",new {},corrida.Version);recuperar.EnsureSuccessStatusCode();
            var actual=(await recuperar.Content.ReadFromJsonAsync<CorridaP21>())!;
            Assert.NotEqual(corrida.PolizaId,actual.PolizaId);if(nueva != null) Assert.Equal(nueva,actual.PolizaId);nueva=actual.PolizaId;
        }
        var cuadra=await cliente.GetFromJsonAsync<ReporteConciliacion>($"{Base}/conciliacion?periodo=2027-02-01");Assert.Equal(0,cuadra!.DepreciacionPorActivo!.Single(a => a.ActivoId == id).Diferencia);
        using var ver=e.Scope();using var lectura=ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Equal(4166.67m,(await ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.SingleAsync(a => a.Id == id)).DepreciacionAcumulada);
        Assert.Equal(3,await ver.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Polizas.CountAsync(p => p.OrigenModulo == "ActivosFijos"));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task P16_anular_carga_rechaza_con_corrida_y_libera_huella_sin_corrida(bool depreciar)
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();await Preparar(e,false);
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var datos=new SolicitudCarga(CargaLibroServicio.Plantilla+$"P16-CARGA,154,Equipo prueba,2026-01-01,2026-01-01,100,0,,{e.SucursalId},Prueba,,,,,", "P16",new(2027,1,31),[]);
        using var r=await Post(cliente,"carga/confirmar",datos);r.EnsureSuccessStatusCode();var lote=(await r.Content.ReadFromJsonAsync<ResultadoCarga>())!;Assert.True(lote.Valida);
        if(depreciar)
        {
            // Completa la custodia del saldo importado con el centro de la prueba.
            using var scope=e.Scope();using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
            var a=await db.Activos.SingleAsync(a => a.Numero == "P16-CARGA");
            db.Add(new CorridaDepreciacion { EmpresaId=a.EmpresaId,Periodo=new(2027,2,1),Revision=1,Detalles=[new() {EmpresaId=a.EmpresaId,ActivoId=a.Id,Numero=a.Numero,ClaseId=a.ClaseId,Importe=1}] });await db.SaveChangesAsync();
        }
        var cargas=await cliente.GetFromJsonAsync<List<CargaP21>>($"{Base}/carga");var carga=cargas!.Single(c => c.Id == lote.CargaId);
        using var anular=await Post(cliente,$"carga/{carga.Id}/anular",new {motivo="Corregir archivo"},carga.Version);
        Assert.Equal(depreciar ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.OK,anular.StatusCode);
        if(!depreciar) {using var recargar=await Post(cliente,"carga/confirmar",datos);recargar.EnsureSuccessStatusCode();var nuevo=(await recargar.Content.ReadFromJsonAsync<ResultadoCarga>())!;Assert.True(nuevo.Valida);Assert.False(nuevo.Idempotente);Assert.NotEqual(lote.CargaId,nuevo.CargaId);}
    }
    [Fact] public async Task P16_acepta_con_causa_responsable_inmutable_y_detecta_cambio()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var reporte=(await cliente.GetFromJsonAsync<ReporteConciliacion>($"{Base}/conciliacion?periodo=2027-01-01&sucursalId={e.SucursalId}"))!;
        var diferencias=reporte.Clases.Where(c => c.DiferenciaValor != 0 || c.DiferenciaAcumulada != 0).Select(c => new ClasificacionDiferencia(c.ClaseId,"Carga inicial",e.Responsable)).ToList();
        using var registrar=await Post(cliente,"conciliacion",new {periodo="2027-01-01",sucursalId=e.SucursalId,diferencias});registrar.EnsureSuccessStatusCode();
        var guardada=(await registrar.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using var editar=await cliente.PutAsJsonAsync($"{Base}/conciliacion/{guardada}",new {});Assert.Equal(HttpStatusCode.UnprocessableEntity,editar.StatusCode);
        using(var scope=e.Scope()) {using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();(await db.Activos.SingleAsync(a => a.Id == id)).EditarTasas(25,null);await db.SaveChangesAsync();}
        // Una tasa futura no cambia los saldos ya aceptados.
        var historial=(await cliente.GetFromJsonAsync<List<JsonElement>>($"{Base}/conciliacion/historial?periodo=2027-01-01&sucursalId={e.SucursalId}"))!;Assert.Equal("Aceptada",historial.Single().GetProperty("estado").GetString());
        using(var scope=e.Scope()) {using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();var a=await db.Activos.SingleAsync(a => a.Id == id);db.Add(new CorridaDepreciacion {EmpresaId=a.EmpresaId,Periodo=new(2027,1,1),Revision=1,Detalles=[new() {EmpresaId=a.EmpresaId,ActivoId=a.Id,Numero=a.Numero,ClaseId=a.ClaseId,SucursalId=a.SucursalId,CentroCostoId=a.CentroCostoId,Importe=1}]});await db.SaveChangesAsync();}
        var desactualizada=(await cliente.GetFromJsonAsync<List<JsonElement>>($"{Base}/conciliacion/historial?periodo=2027-01-01&sucursalId={e.SucursalId}"))!;Assert.Equal("Desactualizada",desactualizada.Single().GetProperty("estado").GetString());
    }
    [Fact] public async Task P16_version_requiere_otra_persona_y_aplica_por_mes()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var activoId=await Preparar(e,false);
        using var captura=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);using var autoriza=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var activo=(await captura.GetFromJsonAsync<ActivoP16>($"{Base}/activos/{activoId}"))!;
        var clase=(await captura.GetFromJsonAsync<List<ClaseP16>>($"{Base}/clases"))!.Single(c => c.Id == activo.ClaseId);
        async Task<HttpResponseMessage> Proponer(DateOnly vigencia,decimal tasa,string? motivo = "Actualización de política")
        {
            using var req=new HttpRequestMessage(HttpMethod.Put,$"{Base}/clases/{clase.Id}") {Content=JsonContent.Create(new ClaseActivoDatos(clase.Clave,clase.Nombre,10,clase.CuentaActivoId,clase.CuentaDepreciacionId,clase.CuentaGastoId,clase.CuentaPuenteId,true,true,true,tasa,vigencia,motivo))};
            req.Headers.Add("If-Match",$"\"{clase.Version}\"");req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());return await captura.SendAsync(req);
        }
        using var sinMotivo=await Proponer(new(2027,3,1),25,null);Assert.Equal(HttpStatusCode.UnprocessableEntity,sinMotivo.StatusCode);
        using var propuesta=await Proponer(new(2027,3,1),25);propuesta.EnsureSuccessStatusCode();clase=(await propuesta.Content.ReadFromJsonAsync<ClaseP16>())!;
        var lecturaClases=(await captura.GetFromJsonAsync<List<JsonElement>>($"{Base}/clases"))!;
        var lecturaClase=lecturaClases.Single(c => c.GetProperty("id").GetGuid() == clase.Id);
        Assert.True(lecturaClase.GetProperty("propuestaPendiente").GetBoolean());
        Assert.Equal("2027-03-01",lecturaClase.GetProperty("proximaVersionDesde").GetString());
        Assert.Equal(10,lecturaClase.GetProperty("tasaContableAnual").GetDecimal());
        var v=clase.Versiones.Single(v => v.Estado == EstadoVersionClase.EnRevision);
        using var misma=await Post(captura,$"clases/{clase.Id}/versiones/{v.Id}/resolver",new {autorizar=true},v.Version);Assert.Equal(HttpStatusCode.UnprocessableEntity,misma.StatusCode);
        using var otra=await Post(autoriza,$"clases/{clase.Id}/versiones/{v.Id}/resolver",new {autorizar=true},v.Version);otra.EnsureSuccessStatusCode();
        using var febrero=await Post(captura,"depreciacion",new {periodo="2027-02-01"});febrero.EnsureSuccessStatusCode();Assert.Equal(4166.67m,(await febrero.Content.ReadFromJsonAsync<CorridaDepreciacion>())!.Detalles.Single(d => d.ActivoId == activoId).Importe);
        using var marzo=await Post(captura,"depreciacion",new {periodo="2027-03-01"});marzo.EnsureSuccessStatusCode();Assert.Equal(10416.67m,(await marzo.Content.ReadFromJsonAsync<CorridaDepreciacion>())!.Detalles.Single(d => d.ActivoId == activoId).Importe);
        clase=(await captura.GetFromJsonAsync<List<ClaseP16>>($"{Base}/clases"))!.Single(c => c.Id == clase.Id);
        using var retroactiva=await Proponer(new(2027,3,1),30);Assert.Equal(HttpStatusCode.UnprocessableEntity,retroactiva.StatusCode);
        using var rechazar=await Proponer(new(2027,4,1),30);rechazar.EnsureSuccessStatusCode();clase=(await rechazar.Content.ReadFromJsonAsync<ClaseP16>())!;v=clase.Versiones.Single(v => v.Estado == EstadoVersionClase.EnRevision);
        using var rechazo=await Post(autoriza,$"clases/{clase.Id}/versiones/{v.Id}/resolver",new {autorizar=false,motivo="Conservar tasa anterior"},v.Version);rechazo.EnsureSuccessStatusCode();
        var abril=(await captura.GetFromJsonAsync<List<DetalleDepreciacion>>($"{Base}/depreciacion/previa?periodo=2027-04-01"))!;Assert.Equal(25,abril.Single(d => d.ActivoId == activoId).Tasa);
    }

    [Fact] public async Task P16_tasas_explicitas_mandan_sobre_mensual_y_equivalencia_editable()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();await Preparar(e,false);
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using(var req=new HttpRequestMessage(HttpMethod.Put,$"{Base}/clases/equivalencias-sap") {Content=JsonContent.Create(new {claseSap="SAP PRUEBA",claveErp="153"})})
        {req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());using var respuesta=await cliente.SendAsync(req);respuesta.EnsureSuccessStatusCode();}
        string[] valores=["P16-EXPLICITA","SAP PRUEBA","Equipo","2026-01-01","2026-01-01","28594.44","0","",e.SucursalId.ToString(),"Prueba","","","no","","no","15","12","595.75"];
        var csv=CargaLibroServicio.Plantilla.TrimEnd()+",Tasa contable,Tasa fiscal,Depreciación normal\n"+string.Join(",",valores);
        using var previa=await Post(cliente,"carga/previa",new SolicitudCarga(csv,"P16",new(2027,1,31),[]));previa.EnsureSuccessStatusCode();var r=(await previa.Content.ReadFromJsonAsync<ResultadoCarga>())!;
        Assert.True(r.Valida);var activo=Assert.Single(r.Activos!);Assert.Equal("153",activo.Clase);Assert.Equal(15,activo.TasaContablePropia);Assert.Equal(12,activo.TasaFiscalPropia);
    }
    private sealed record CorridaP21(Guid Id, int Version, Guid? PolizaId, bool SinEfectoContable);
    private sealed record CargaP21(Guid Id, int Version);
    private sealed record ActivoP16(Guid ClaseId);
    private sealed record VersionP16(Guid Id,int Version,EstadoVersionClase Estado);
    private sealed record ClaseP16(Guid Id,int Version,string Clave,string Nombre,Guid? CuentaActivoId,Guid? CuentaDepreciacionId,Guid? CuentaGastoId,Guid? CuentaPuenteId,List<VersionP16> Versiones);

}
