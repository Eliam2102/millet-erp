using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.Notificaciones;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// 86e3tac2y con Postgres real: el aviso llega solo a quien tiene el permiso del rol (y la sucursal del vale), cada usuario lee
/// solo los suyos y el acta del cierre solo la marca revisada quien tiene <c>almacen.cierre.revisar-acta</c>.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class AvisosAlmacenTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = ContabTestKit.EmpresaBootstrapId;

    [Fact]
    public async Task Ajuste_nivel_3_llega_a_Finanzas_con_folio_y_monto_y_a_nadie_mas()
    {
        var finanzas = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenAvisosAjusteMayor);
        var jefe = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenAvisosValeVencido);
        try
        {
            var conteoId = Guid.NewGuid();
            using (var scope = factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var servicio = scope.ServiceProvider.GetRequiredService<AvisosInternosNotificacionService>();
                var aviso = new AvisoAjusteNivel3(Empresa, conteoId, 12000m, ContabTestKit.UsuarioIdDe(jefe), DateTimeOffset.UtcNow, []);
                await servicio.NotificarAjusteNivel3AprobadoAsync(aviso, default);
                await servicio.NotificarAjusteNivel3AprobadoAsync(aviso, default);
            }

            var propios = await ContabTestKit.Json(await finanzas.GetAsync("/api/v1/almacen/avisos"));
            var aviso1 = Assert.Single(propios.EnumerateArray());
            Assert.Equal("ajuste-mayor", aviso1.GetProperty("tipo").GetString());
            Assert.Contains(AvisosInternosNotificacionService.FolioConteo(conteoId), aviso1.GetProperty("mensaje").GetString());
            Assert.Contains("12,000.00", aviso1.GetProperty("mensaje").GetString());
            Assert.False(aviso1.GetProperty("leido").GetBoolean());

            Assert.Equal(0, (await ContabTestKit.Json(await jefe.GetAsync("/api/v1/almacen/avisos"))).GetArrayLength());

            using var leer = await finanzas.PostAsync($"/api/v1/almacen/avisos/{aviso1.GetProperty("id").GetGuid()}/leer", null);
            Assert.Equal(HttpStatusCode.NoContent, leer.StatusCode);
            Assert.True((await ContabTestKit.Json(await finanzas.GetAsync("/api/v1/almacen/avisos")))[0].GetProperty("leido").GetBoolean());
            // El aviso de otro usuario no se puede marcar.
            using var ajeno = await jefe.PostAsync($"/api/v1/almacen/avisos/{aviso1.GetProperty("id").GetGuid()}/leer", null);
            Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(finanzas);
            await ContabTestKit.LimpiarClienteAsync(jefe);
        }
    }

    [Fact]
    public async Task Destinatarios_del_vale_son_los_del_permiso_asignados_a_la_sucursal()
    {
        var jefe = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenAvisosValeVencido);
        var jefeId = ContabTestKit.UsuarioIdDe(jefe);
        Guid sucursalId;
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var puerto = scope.ServiceProvider.GetRequiredService<IDestinatariosAvisoReadPort>();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        sucursalId = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Sucursales.AsNoTracking()
            .Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        try
        {
            Assert.Contains(jefeId, await puerto.UsuariosConPermisoAsync(Empresa, PermisosCanonicos.AlmacenAvisosValeVencido, null, default));
            Assert.DoesNotContain(jefeId, await puerto.UsuariosConPermisoAsync(Empresa, PermisosCanonicos.AlmacenAvisosAjusteMayor, null, default));
            Assert.DoesNotContain(jefeId, await puerto.UsuariosConPermisoAsync(Empresa, PermisosCanonicos.AlmacenAvisosValeVencido, sucursalId, default));

            var asignacion = new UsuarioSucursal(Guid.NewGuid(), jefeId, sucursalId, Empresa);
            identidad.Add(asignacion);
            await identidad.SaveChangesAsync();
            Assert.Contains(jefeId, await puerto.UsuariosConPermisoAsync(Empresa, PermisosCanonicos.AlmacenAvisosValeVencido, sucursalId, default));
            await identidad.UsuarioSucursales.Where(a => a.Id == asignacion.Id).ExecuteDeleteAsync();
        }
        finally { await ContabTestKit.LimpiarClienteAsync(jefe); }
    }

    [Fact]
    public async Task Solo_quien_tiene_el_permiso_marca_revisada_el_acta_y_queda_con_usuario_y_fecha()
    {
        var director = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenCierreRevisarActa);
        var ejecutor = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenCierreMesEjecutar);
        var directorId = ContabTestKit.UsuarioIdDe(director);
        var actaId = Guid.NewGuid();
        var anio = Random.Shared.Next(2100, 2900);
        var mes = Random.Shared.Next(1, 13);
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            var periodo = new PeriodoCerrado(Guid.NewGuid(), Empresa, anio, mes, ContabTestKit.UsuarioIdDe(ejecutor));
            db.Add(periodo);
            db.Add(new ActaCierreMes(periodo, """{"almacenes":[],"conteos":[],"vales":[]}"""));
            await db.SaveChangesAsync();
            actaId = await db.ActasCierre.Where(a => a.PeriodoCerradoId == periodo.Id).Select(a => a.Id).SingleAsync();
        }
        try
        {
            using var sinPermiso = await ejecutor.PostAsync($"/api/v1/almacen/cierre-mes/acta/{actaId}/revisar", null);
            Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);

            using var ok = await director.PostAsync($"/api/v1/almacen/cierre-mes/acta/{actaId}/revisar", null);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var j = await ContabTestKit.Json(ok);
            Assert.Equal("REVISADA", j.GetProperty("estado").GetString());
            Assert.Equal(directorId, j.GetProperty("revisadaPorId").GetGuid());
            Assert.NotEqual(JsonValueKind.Null, j.GetProperty("revisadaAt").ValueKind);

            using var otra = await director.PostAsync($"/api/v1/almacen/cierre-mes/acta/{actaId}/revisar", null);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otra.StatusCode);

            using var lista = await ejecutor.GetAsync("/api/v1/almacen/cierre-mes/actas");
            Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            await db.ActasCierre.Where(a => a.Id == actaId).ExecuteDeleteAsync();
            await db.PeriodosCerrados.Where(p => p.Anio == anio && p.Mes == mes).ExecuteDeleteAsync();
            await ContabTestKit.LimpiarClienteAsync(director);
            await ContabTestKit.LimpiarClienteAsync(ejecutor);
        }
    }
}
