using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Millet.Facturacion.Domain.Ports;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Api.Seed;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
public sealed partial class LibroHttpTests
{
    private const string Base = "/api/v1/activos-fijos";
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CA1_CA2_CA3_CA12_Corrida_real_unica_anulacion_recalculo_y_conciliacion(bool cerrado)
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var cliente = await e.ClienteAsync(true, permisosCe:false, permisosAf:true);
        var id = await Preparar(e, cerrado);
        var previa = await cliente.GetFromJsonAsync<List<DetalleDepreciacion>>($"{Base}/depreciacion/previa?periodo=2027-02-01");
        Assert.Equal(4166.67m, previa!.Single(x=>x.ActivoId==id).Importe);
        using var corrida = await Post(cliente,"depreciacion",new { periodo="2027-02-01" });
        if (cerrado) { Assert.Equal(HttpStatusCode.UnprocessableEntity,corrida.StatusCode); return; }
        corrida.EnsureSuccessStatusCode(); var c = await corrida.Content.ReadFromJsonAsync<JsonElement>();
        using var repetida = await Post(cliente,"depreciacion",new { periodo="2027-02-01" }); repetida.EnsureSuccessStatusCode();
        Assert.Equal(c.GetProperty("id").GetGuid(),(await repetida.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var reporte = await cliente.GetFromJsonAsync<ReporteConciliacion>($"{Base}/conciliacion?periodo=2027-02-01");
        var maq = reporte!.Clases.Single(x=>x.Clase=="154"); Assert.Equal(0,maq.DiferenciaAcumulada); Assert.Equal(500000,maq.DiferenciaValor); // Alta de prueba sin póliza.
        using var anular = await Post(cliente,$"depreciacion/{c.GetProperty("id").GetGuid()}/anular",new { motivo="FIX corrección" },c.GetProperty("version").GetInt32()); anular.EnsureSuccessStatusCode();
        using var nueva = await Post(cliente,"depreciacion",new { periodo="2027-02-01" }); nueva.EnsureSuccessStatusCode();
        Assert.Equal(2,(await nueva.Content.ReadFromJsonAsync<CorridaDepreciacion>())!.Revision);
        using var scope=e.Scope(); using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Equal(4166.67m,(await scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.SingleAsync(x=>x.Id==id)).DepreciacionAcumulada);
        var polizas=await scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Polizas.Include(x=>x.Partidas).Where(x=>x.OrigenModulo=="ActivosFijos").ToListAsync();
        Assert.Equal(3,polizas.Count); Assert.All(polizas,p=>Assert.Equal(p.Partidas.Sum(x=>x.Cargo),p.Partidas.Sum(x=>x.Abono)));
    }
    [Fact] public async Task CA14_Sin_permiso_o_sucursal_ajena_rechaza_corrida_y_operaciones()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync(); var id=await Preparar(e,false);
        foreach(var permiso in new[]{false,true})
        {
            using var cliente=await e.ClienteAsync(permiso,asociar:!permiso,permisosCe:false,permisosAf:true);
            using var r=await Post(cliente,"depreciacion",new{periodo="2027-02-01"}); Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode);
            using var op=await Post(cliente,$"activos/{id}/operaciones",new { tipo="Amortizacion", fecha="2027-03-01", importe=100, motivo="FIX", evidencia=(string?)null },1); Assert.Equal(HttpStatusCode.Forbidden,op.StatusCode);
        }
    }
    [Fact] public async Task CA10_CA11_Carga_SAP_seca_atomica_preserva_numeros_idempotente_sin_poliza()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync(); var id=await Preparar(e,false);
        using var cliente=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        Guid centro; using(var scope=e.Scope()) { using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass(); centro=(await scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.SingleAsync(x=>x.Id==id)).CentroCostoId; }
        string Csv(string clase) => CargaLibroServicio.Plantilla+$"SAP-PRUEBA,{clase},Montacargas SAP,2026-01-01,2026-01-15,500000,100000,{e.Responsable},{e.SucursalId},Conkal,{centro},,no,,no\n";
        var malo=new SolicitudCarga(Csv("INEXISTENTE"),"1",new(2026,12,31),[]);
        using var prueba=await Post(cliente,"carga/previa",malo); prueba.EnsureSuccessStatusCode(); var resultado=await prueba.Content.ReadFromJsonAsync<ResultadoCarga>(); Assert.False(resultado!.Valida); Assert.Single(resultado.Errores);
        var bueno=malo with { Csv=Csv("154"),Totales=[new("154",500000,100000)] };
        using var carga=await Post(cliente,"carga/confirmar",bueno); carga.EnsureSuccessStatusCode(); var r=await carga.Content.ReadFromJsonAsync<ResultadoCarga>(); Assert.True(r!.Valida); Assert.Equal(0,r.Cuadre.Single().DiferenciaValor); Assert.Equal(0,r.Cuadre.Single().DiferenciaAcumulada);
        using var repetir=await Post(cliente,"carga/confirmar",bueno); repetir.EnsureSuccessStatusCode(); Assert.True((await repetir.Content.ReadFromJsonAsync<ResultadoCarga>())!.Idempotente);
        using var ver=e.Scope(); using var lectura=ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass(); var db=ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        var a=await db.Activos.SingleAsync(x=>x.Numero=="SAP-PRUEBA"); Assert.Equal(400000,a.ValorEnLibros); Assert.Null(a.PolizaAltaId);
        Assert.Empty(await ver.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Polizas.Where(x=>x.OrigenModulo=="ActivosFijos").ToListAsync());
    }
    [Fact] public async Task CA7_Venta_deprecia_hasta_mayo_baja_con_utilidad_y_cancelacion_reintegra()
    {
        var venta=new VentaFixture();
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync(s=>s.Replace(ServiceDescriptor.Singleton<IVentaActivoReadPort>(venta)));
        var id=await Preparar(e,false,233333.32m); var factura=Guid.NewGuid();
        async Task Consumir()
        {
            using var scope=e.Scope();var sp=scope.ServiceProvider;var accessor=sp.GetRequiredService<IHttpContextAccessor>();var previo=accessor.HttpContext;
            try { accessor.HttpContext=new DefaultHttpContext { User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(MilletClaimTypes.CurrentEmpresaId,DemoSesionSeedHostedService.EmpresaId.ToString())],"FIX")) };
                await sp.GetRequiredService<VentaActivoConsumer>().ProcesarAsync(factura,default); }
            finally { accessor.HttpContext=previo; }
        }
        await Consumir(); await Consumir();
        using(var scope=e.Scope())
        {
            var sp=scope.ServiceProvider;using var bypass=sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();var db=sp.GetRequiredService<ActivosFijosDbContext>();
            var a=await db.Activos.SingleAsync(x=>x.Id==id);Assert.Equal(250000,a.DepreciacionAcumulada);Assert.Equal(EstadoActivo.DadoDeBaja,a.Estado);
            Assert.Equal(4,await db.Set<CorridaDepreciacion>().CountAsync());Assert.Single(await db.Set<OperacionLibro>().ToListAsync());
            var poliza=await sp.GetRequiredService<ContabilidadDbContext>().Polizas.Include(x=>x.Partidas).SingleAsync(x=>x.OrigenTipo=="Venta");
            Assert.Contains(poliza.Partidas,p=>p.Abono==50000);Assert.Equal(poliza.TotalCargos,poliza.TotalAbonos);
        }
        venta.Cancelada=true;await Consumir();await Consumir();
        using var ver=e.Scope();using var lectura=ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();var af=ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        Assert.Equal(EstadoActivo.Activo,(await af.Activos.SingleAsync(x=>x.Id==id)).Estado);Assert.True((await af.Set<OperacionLibro>().SingleAsync()).Anulada);
    }
    [Fact] public async Task CA9_CA13_Baja_con_evidencia_y_segundo_autorizador_registra_perdida_revalorizacion_apagada()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();var id=await Preparar(e,false);
        using var captura=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        using var autorizador=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var activo=await captura.GetFromJsonAsync<JsonElement>($"{Base}/activos/{id}");var version=activo.GetProperty("version").GetInt32();
        using var sinEvidencia=await Post(captura,$"activos/{id}/operaciones",new {tipo="Baja",fecha="2027-02-01",importe=0,motivo="Siniestro",evidencia=(string?)null},version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,sinEvidencia.StatusCode);
        using var revaluar=await Post(captura,$"activos/{id}/operaciones",new {tipo="Revalorizacion",fecha="2027-02-01",importe=100,motivo="FIX ajuste",evidencia=(string?)null},version);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,revaluar.StatusCode);
        using var registro=await Post(captura,$"activos/{id}/operaciones",new {tipo="Baja",fecha="2027-02-01",importe=0,motivo="Siniestro",evidencia="data:application/pdf;base64,JVBERi0xLjc="},version);
        registro.EnsureSuccessStatusCode();var op=await registro.Content.ReadFromJsonAsync<JsonElement>();var ruta=$"operaciones/{op.GetProperty("id").GetGuid()}/autorizar";var v=op.GetProperty("version").GetInt32();
        using var misma=await Post(captura,ruta,new{},v);Assert.Equal(HttpStatusCode.UnprocessableEntity,misma.StatusCode);
        using var otra=await Post(autorizador,ruta,new{},v);otra.EnsureSuccessStatusCode();
        using var ver=e.Scope();using var bypass=ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Equal(EstadoActivo.DadoDeBaja,(await ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.SingleAsync(x=>x.Id==id)).Estado);
        var poliza=await ver.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Polizas.Include(x=>x.Partidas).SingleAsync(x=>x.OrigenTipo=="Baja");Assert.Equal(500000,poliza.TotalCargos);Assert.Equal(poliza.TotalCargos,poliza.TotalAbonos);
    }
    private sealed class VentaFixture:IVentaActivoReadPort
    {
        public bool Cancelada {get;set;}
        public Task<VentaActivoLectura?> ObtenerAsync(Guid factura,CancellationToken ct)=>Task.FromResult<VentaActivoLectura?>(new(DemoSesionSeedHostedService.EmpresaId,"FIX-P4-MONTACARGAS",300000,new(2027,6,1),Cancelada));
    }
    private static async Task<HttpResponseMessage> Post(HttpClient cliente,string ruta,object body,int? version=null)
    {
        using var req=new HttpRequestMessage(HttpMethod.Post,$"{Base}/{ruta}"){Content=JsonContent.Create(body)};
        req.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());if(version!=null)req.Headers.TryAddWithoutValidation("If-Match",$"\"{version}\"");return await cliente.SendAsync(req);
    }
    private static async Task<Guid> Preparar(ExpedientesHttpTests.Escenario e,bool cerrado, decimal? acumulada = null)
    {
        using var scope=e.Scope();var sp=scope.ServiceProvider;using var bypass=sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();var empresa=DemoSesionSeedHostedService.EmpresaId;
        var contab=sp.GetRequiredService<ContabilidadDbContext>();
        CuentaContable Cuenta(string sufijo,NaturalezaCuenta naturaleza) => new(Guid.NewGuid(),"FIX-P4-"+sufijo,"FIX P4 "+sufijo,null,2,naturaleza,TipoCuenta.Afectable,CuentaControl.Ninguna,null,null){EmpresaId=empresa};
        var activo=Cuenta("A",NaturalezaCuenta.Deudora);var dep=Cuenta("D",NaturalezaCuenta.Acreedora);var gasto=Cuenta("G",NaturalezaCuenta.Deudora);contab.Cuentas.AddRange(activo,dep,gasto);
        var ejercicio=new EjercicioContable(Guid.NewGuid(),2027){EmpresaId=empresa};contab.Ejercicios.Add(ejercicio);
        foreach(var p in ejercicio.GenerarPeriodos()){p.EmpresaId=empresa;if(!cerrado && p.Numero <= 12)p.Abrir(null,null,e.Responsable,"FIX apertura P4",DateTimeOffset.UtcNow);contab.Periodos.Add(p);}
        var centros=sp.GetRequiredService<CentrosCostoDbContext>();var centro=new Dim1(Guid.NewGuid(),"FIXP4","FIX ubicación P4");centros.Dim1s.Add(centro);await centros.SaveChangesAsync();
        contab.UbicacionesSucursal.Add(new UbicacionSucursal(Guid.NewGuid(),centro.Id,e.SucursalId){EmpresaId=empresa});await contab.SaveChangesAsync();
        var db=sp.GetRequiredService<ActivosFijosDbContext>();var clase=await db.Clases.SingleAsync(x=>x.EmpresaId==empresa&&x.Clave=="154");clase.Editar(clase.Nombre,10,activo.Id,dep.Id,gasto.Id,gasto.Id,true);
        var a=new Activo(Guid.NewGuid(),empresa,"FIX-P4-MONTACARGAS",new("FIX Montacargas",clase.Id,null,null,null,new(2027,1,1),new(2027,1,15),500000,"MXN",1,new(e.Responsable,e.SucursalId,"Conkal",centro.Id,null),TipoImportacionActivo.No,null,false,"FIX"));if(acumulada is decimal depInicial) a.CargarLibro(new(2027,1,31),depInicial); else a.Capitalizar(Guid.NewGuid());db.Add(a);
        db.Add(new ConfiguracionLibro { EmpresaId=empresa, CuentaUtilidadId=gasto.Id, CuentaPerdidaId=gasto.Id, CuentaVentaPuenteId=gasto.Id });
        await db.SaveChangesAsync();return a.Id;
    }
}
