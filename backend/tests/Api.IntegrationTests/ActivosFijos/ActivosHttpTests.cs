using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Almacen.Application.Integration;
using Millet.Api.Seed;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
/// <summary>Ejecutar con PostgreSQL desechable y MILLET_CE_ENTRA_TESTS=1. No usa una base de desarrollo.</summary>
public sealed class ActivosHttpTests
{
    [Fact]
    public async Task Sin_permiso_o_sucursal_ajena_devuelve_403()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();
        var id=DemoSesionSeedHostedService.Id("DEMO-AF-MAQ");
        using var ninguno=await e.ClienteAsync(false);
        using var ajeno=await e.ClienteAsync(true,asociar:false,permisosCe:false,permisosAf:true);
        using var propio=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        Assert.Equal(HttpStatusCode.Forbidden,(await ninguno.GetAsync($"/api/v1/activos-fijos/activos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await ajeno.GetAsync($"/api/v1/activos-fijos/activos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await ajeno.GetAsync($"/api/v1/activos-fijos/activos?sucursalId={e.SucursalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await propio.GetAsync($"/api/v1/activos-fijos/activos/{id}")).StatusCode);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recepcion_mixta_y_factura_en_ambos_ordenes_crean_un_solo_activo_con_valor_factura(bool facturaPrimero)
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope=e.Scope(); using var bypass=scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db=scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
        var consumer=scope.ServiceProvider.GetRequiredService<CompraActivoConsumer>();
        var empresa=DemoSesionSeedHostedService.EmpresaId; var oc=DemoSesionSeedHostedService.Id("DEMO-AF-OC-MIXTA");
        var linea=DemoSesionSeedHostedService.Id("DEMO-AF-MONTACARGAS"); var factura=Guid.NewGuid();var recepcion=Guid.NewGuid();
        var evento=new OcRecepcionRegistradaIntegrationEvent(empresa,DateTimeOffset.UtcNow,recepcion,"FIX-AF-REC",oc,new DateOnly(2026,1,1),true,null,null,null,
            [new LineaRecepcionPayload(Guid.NewGuid(),DemoSesionSeedHostedService.Id("DEMO-AF-SILICON"),Guid.NewGuid(),"PZA",10,100,1000,Guid.NewGuid()),
             new LineaRecepcionPayload(Guid.NewGuid(),linea,Guid.NewGuid(),"PZA",1,490000,490000,Guid.Empty,EsActivoFijo:true,ClaseActivoId:Guid.Parse("0000000f-0002-0000-0000-000000000002"))],SucursalId:e.SucursalId);
        var facturado=new FacturaConciliadaActivo(empresa,factura,"MXN",1,[new LineaFacturaActivo(Guid.NewGuid(),linea,1,500000,500000)]);
        if(facturaPrimero) await consumer.ConciliarAsync(facturado,CancellationToken.None);
        await consumer.RecibirAsync(evento,CancellationToken.None);await consumer.RecibirAsync(evento,CancellationToken.None);
        await consumer.ConciliarAsync(facturado,CancellationToken.None);await consumer.ConciliarAsync(facturado,CancellationToken.None);
        var a=Assert.Single(await db.Activos.Where(x=>x.RecepcionId==recepcion).ToListAsync());
        Assert.Equal(EstadoActivo.PorCapitalizar,a.Estado);Assert.Equal(500000,a.ValorOriginal);Assert.Equal(factura,a.FacturaProveedorId);
        a.Completar(new DatosActivo(a.Descripcion,a.ClaseId,null,null,null,a.FechaAdquisicion,new DateOnly(2026,1,15),a.ValorMonedaOriginal,a.Moneda,a.TipoCambio,
            new CustodiaActivo(e.Responsable,e.SucursalId,"FIX",Guid.NewGuid(),null),TipoImportacionActivo.Definitivo,"FIX-PEDIMENTO",false,null));
        a.Capitalizar(Guid.NewGuid());await db.SaveChangesAsync();
        var lector=scope.ServiceProvider.GetRequiredService<IActivosFijosReadPort>();var lectura=await lector.ObtenerAsync(a.Numero,CancellationToken.None);
        Assert.NotNull(lectura);Assert.Equal(500000,lectura.ValorEnLibros);Assert.Equal(0,lectura.DepreciacionAcumulada);Assert.True(lectura.EsImportacion);Assert.Equal(a.Descripcion,lectura.Descripcion);
    }
    [Fact]
    public async Task Capitalizar_sin_cuentas_rechaza_y_conserva_por_capitalizar()
    {
        await using var e=await ExpedientesHttpTests.Escenario.CrearAsync();using var c=await e.ClienteAsync(true,permisosCe:false,permisosAf:true);
        var id=DemoSesionSeedHostedService.Id("DEMO-AF-MAQ");using var detalle=await c.GetAsync($"/api/v1/activos-fijos/activos/{id}");
        using var request=new HttpRequestMessage(HttpMethod.Post,$"/api/v1/activos-fijos/activos/{id}/capitalizar"){Content=JsonContent.Create(new{})};
        request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());request.Headers.TryAddWithoutValidation("If-Match",detalle.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await c.SendAsync(request)).StatusCode);
        using var json=JsonDocument.Parse(await c.GetStringAsync($"/api/v1/activos-fijos/activos/{id}"));Assert.Equal(0,json.RootElement.GetProperty("estado").GetInt32());Assert.Equal(JsonValueKind.Null,json.RootElement.GetProperty("polizaAltaId").ValueKind);
    }
}
