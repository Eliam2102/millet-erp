using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Application;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;
namespace Millet.Api.IntegrationTests.CentrosCosto;

/// <summary>
/// ADM08 cambia los permisos en caché de <c>dev-superadmin</c> (le quita el alcance total de centros de costo).
/// Otras clases usan ese mismo usuario en paralelo; por eso esta corre sola, después de las demás.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ADM08SinParalelo { public const string Nombre = "ADM08 sin paralelo"; }

/// <summary>ADM08: PostgreSQL desechable, fixtures propias y limpieza de catálogos compartidos.</summary>
[Collection(ADM08SinParalelo.Nombre)]
public sealed class ADM08DepartamentoEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/api/v1/centros-costo/equivalencias/opciones")]
    [InlineData("/api/v1/compras/ordenes/centros-costo/buscar")]
    [InlineData("/api/v1/compras/requisiciones/centros-costo/buscar")]
    public async Task Nuevos_selectores_exigen_autenticacion(string path)
    {
        using var client = factory.CreateClientWithIdempotency();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Equivalencia_Rq_herencia_solo_lectura_alcance_congelamiento_y_vigencia()
    {
        using var client = factory.CreateClientWithIdempotency();
        var loginResponse = await client.PostAsJsonAsync("/api/dev/fake-login", new { entraOid = "dev-superadmin", email = "dev-superadmin@dev.local", nombre = "ADM08 DEMO" });
        loginResponse.EnsureSuccessStatusCode(); var login = await JsonAsync(loginResponse);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        var usuario = login.GetProperty("usuario").GetProperty("id").GetGuid();
        var empresa = login.GetProperty("empresas").EnumerateArray().Single(x => x.GetProperty("esLaActual").GetBoolean()).GetProperty("id").GetGuid();
        using var scope = factory.Services.CreateScope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var org = sp.GetRequiredService<CompartidoDbContext>(); var db = sp.GetRequiredService<CentrosCostoDbContext>();
        var compras = sp.GetRequiredService<ComprasDbContext>(); var permisos = sp.GetRequiredService<IPermissionCache>();
        var sucursal = await org.Sucursales.FirstAsync(x => x.EmpresaId == empresa);
        var depto = Guid.CreateVersion7(); var planta = Guid.CreateVersion7(); var area = Guid.CreateVersion7(); var maquina = Guid.CreateVersion7();
        var g2 = Guid.CreateVersion7(); var g3 = Guid.CreateVersion7(); var rqId = Guid.CreateVersion7();
        var sufijo = Guid.NewGuid().ToString("N")[..7];
        var permisosSinAlcance = PermisosCanonicos.Todos.Select(x => x.Codigo).Where(x => x != PermisosCanonicos.CentrosCostoDim3LeerTodos).ToArray();
        var eqPath = $"/api/v1/centros-costo/equivalencias/{sucursal.Id}/{depto}";
        try
        {
            org.Departamentos.Add(new Departamento(depto, empresa, $"A{sufijo}", "ADM08 DEMO departamento"));
            org.SucursalDepartamentos.Add(new SucursalDepartamento(Guid.CreateVersion7(), empresa, sucursal.Id, depto));
            await org.SaveChangesAsync();
            db.GruposDim2.Add(new GrupoDim2(g2, $"ADM08 DEMO {sufijo}")); db.GruposDim3.Add(new GrupoDim3(g3, $"ADM08 DEMO {sufijo}"));
            db.Dim1s.Add(new Dim1(planta, $"A{sufijo}", "ADM08 DEMO planta"));
            db.Dim2s.Add(new Dim2(area, planta, $"B{sufijo}", "ADM08 DEMO área", g2));
            db.Dim3s.Add(new Dim3(maquina, area, $"M{sufijo}", "ADM08 DEMO máquina", g3));
            await db.SaveChangesAsync();
            // No puede asociar una máquina como equivalencia del departamento.
            using var invalida = await GuardarAsync(client, eqPath, maquina, "0");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalida.StatusCode);
            using var creada = await GuardarAsync(client, eqPath, area, "0"); creada.EnsureSuccessStatusCode();
            var eqId = await db.DepartamentoCentrosCosto.AsNoTracking().Where(x => x.DepartamentoId == depto).Select(x => x.Id).SingleAsync();
            Assert.True(await db.Set<AuditLogEntry>().AnyAsync(x => x.EntidadId == eqId && x.UsuarioId == usuario));
            await permisos.SetAsync(usuario, empresa, permisosSinAlcance.Where(x => x != PermisosCanonicos.CentrosCostoCatalogoAdministrar).ToArray());
            using var prohibida = await GuardarAsync(client, eqPath, area, "0");
            Assert.Equal(HttpStatusCode.Forbidden, prohibida.StatusCode);
            await permisos.SetAsync(usuario, empresa, PermisosCanonicos.Todos.Select(x => x.Codigo).ToArray());
            using var desactualizada = await GuardarAsync(client, eqPath, planta, "0");
            Assert.Equal(HttpStatusCode.Conflict, desactualizada.StatusCode);
            using var sinVersion = await client.PutAsJsonAsync(eqPath, new { centroCostoId = area, observaciones = "DEMO" });
            Assert.Equal((HttpStatusCode)428, sinVersion.StatusCode);
            var lista = await client.GetFromJsonAsync<JsonElement>($"/api/v1/centros-costo/equivalencias/{sucursal.Id}");
            var equivalencia = lista.EnumerateArray().Single(x => x.GetProperty("departamentoId").GetGuid() == depto);
            Assert.Contains("V49", equivalencia.GetProperty("observaciones").GetString());

            // Requisitante distinto sin ficha de empleado: usa el departamento ya validado de la RQ.
            var rq = new Requisicion(rqId, empresa, Folio.Parse($"ADM2026-{Random.Shared.Next(100000, 999999)}"), 2026,
                Clasificacion.Servicio, sucursal.Id, depto, null, Guid.NewGuid(), usuario, Prioridad.Normal, DateTimeOffset.UtcNow);
            compras.Requisiciones.Add(rq); await compras.SaveChangesAsync();
            await permisos.SetAsync(usuario, empresa, permisosSinAlcance);
            var contexto = await client.GetFromJsonAsync<CentroCostoCaptura>($"/api/v1/compras/requisiciones/{rqId}/lineas/centro-costo-captura");
            Assert.NotNull(contexto); Assert.False(contexto.PuedeElegir); Assert.Equal(area, contexto.Heredado!.Id);
            var articulo = await org.Articulos.FirstAsync(x => x.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Activo);
            var rutaLineas = $"/api/v1/compras/requisiciones/{rqId}/lineas";
            object Body(Guid? cc) => new { articuloId = articulo.Id, cantidad = 1m, unidadMedida = articulo.UnidadMedidaDefault,
                precioEstimadoMonto = 10m, precioEstimadoMoneda = "MXN", centroCostoId = cc };
            using var heredada = await client.PostAsJsonAsync(rutaLineas, Body(null)); heredada.EnsureSuccessStatusCode();
            var lineaId = (await JsonAsync(heredada)).GetProperty("id").GetGuid();
            var guardada = await compras.LineaRequisiciones.AsNoTracking().SingleAsync(x => x.Id == lineaId);
            Assert.Equal(area, guardada.CentroCostoId);
            using var forzada = await client.PostAsJsonAsync(rutaLineas, Body(maquina));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, forzada.StatusCode);
            Assert.Equal("CECO_SOLO_LECTURA", (await JsonAsync(forzada)).GetProperty("code").GetString());
            await permisos.SetAsync(usuario, empresa, PermisosCanonicos.Todos.Select(x => x.Codigo).ToArray());
            using var elegida = await client.PostAsJsonAsync(rutaLineas, Body(maquina)); elegida.EnsureSuccessStatusCode();
            // El cambio de equivalencia nunca reescribe líneas guardadas.
            using var cambiada = await GuardarAsync(client, eqPath, planta, equivalencia.GetProperty("version").GetInt32().ToString()); cambiada.EnsureSuccessStatusCode();
            Assert.Equal(area, (await compras.LineaRequisiciones.AsNoTracking().SingleAsync(x => x.Id == lineaId)).CentroCostoId);
            var abierta = await client.GetFromJsonAsync<CentroCostoOpcion[]>("/api/v1/compras/ordenes/centros-costo/buscar");
            Assert.Contains(abierta!, x => x.Id == area && x.Nivel == 2);
            using var transmitida = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", new { }); transmitida.EnsureSuccessStatusCode();
            using var edicion = await client.PatchAsJsonAsync($"{rutaLineas}/{lineaId}", Body(planta));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, edicion.StatusCode);
            Assert.Equal("LINEAS_SOLO_EN_BORRADOR", (await JsonAsync(edicion)).GetProperty("code").GetString());
            // Lectura histórica por ID soporta centros sin máquina.
            Assert.Equal(2, (await sp.GetRequiredService<ICentroCostoCapturaPort>().BuscarAsync("DEMO área", true, default)).Single(x => x.Id == area).Nivel);
            (await db.Dim1s.SingleAsync(x => x.Id == planta)).CambiarEstatus(Millet.Catalogos.Domain.EstatusCatalogo.Inactivo); await db.SaveChangesAsync();
            Assert.Equal(Dim3Elegibilidad.Inactiva, await sp.GetRequiredService<IDim3ElegibilidadPort>().EvaluarAsync(area, false, default));
            var historia = await sp.GetRequiredService<IDim3ReadPort>().ObtenerAsync([area], default);
            Assert.Equal($"B{sufijo}", historia[area].Clave); Assert.False(historia[area].Activa);
        }
        finally
        {
            await permisos.InvalidateAsync(usuario, empresa);
            await compras.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisicion_lineas WHERE requisicion_id = {rqId}");
            await compras.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisiciones WHERE id = {rqId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.departamento_centros_costo WHERE departamento_id = {depto}");
            await org.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compartido.sucursal_departamentos WHERE departamento_id = {depto}");
            await org.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compartido.departamentos WHERE id = {depto}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim3 WHERE id = {maquina}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim2 WHERE id = {area}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim1 WHERE id = {planta}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim2 WHERE id = {g2}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim3 WHERE id = {g3}");
        }
    }
    private static async Task<HttpResponseMessage> GuardarAsync(HttpClient client, string path, Guid centro, string version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(new { centroCostoId = centro, observaciones = "DEMO · por validar con Laura (V49)" }) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\""); return await client.SendAsync(request);
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return doc.RootElement.Clone();
    }
}
