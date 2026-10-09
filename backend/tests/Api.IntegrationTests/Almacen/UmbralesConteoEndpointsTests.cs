using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Administracion.Application.Parametros;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>PostgreSQL desechable. Catálogos propios con limpieza y restauración de parámetros en finally.</summary>
public sealed class UmbralesConteoEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Parametros_auditoria_foto_y_aprobacion_por_nivel_con_outbox()
    {
        var avisos = new Avisos();
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            // Autenticación HTTP real de test; sólo sustituimos los permisos del handler
            // para probar cada nivel sin alterar roles ni usuarios compartidos.
            s.RemoveAll<ICurrentUserPermissions>();
            s.AddScoped<ICurrentUserPermissions, PermisosDelRequest>();
            s.RemoveAll<INotificacionService>();
            s.AddSingleton<INotificacionService>(avisos);
        }));
        using var client = app.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev",
            EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            loginJson.RootElement.GetProperty("accessToken").GetString());
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var admin = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var empresa = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IConteoUmbralesProvider>();
        var almacenId = Guid.NewGuid(); var subId = Guid.NewGuid(); var rackId = Guid.NewGuid();
        var articuloId = Guid.NewGuid(); var sucursalId = Guid.NewGuid();
        var conteos = new List<Guid>();
        var originales = await admin.ParametrosGlobales.AsNoTracking()
            .Where(p => ParametrosUmbralesConteo.Claves.Contains(p.Clave)).ToDictionaryAsync(p => p.Clave, p => p.Valor);
        Assert.Equal(4, originales.Count);
        // Empresa del usuario autenticado: evita probar sólo con bypass en el endpoint.
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        var empresaId = me.RootElement.GetProperty("currentEmpresaId").GetGuid();
        var actorId = me.RootElement.GetProperty("userId").GetGuid();
        var clave = $"A45{Guid.NewGuid():N}"[..12];
        using var bypass = empresa.Bypass();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {clave}, 'Prueba A4.5', {sucursalId}, 0, 1, NOW(), NOW())
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subId}, {almacenId}, {clave}, 'Prueba A4.5', 0, 0, 1, NOW(), NOW())
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackId}, {subId}, 'A45', 'Prueba A4.5', 0, false, 1, NOW(), NOW())
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({rackId}, {subId}, {articuloId}, 20000, 1, NOW())
                """);

            async Task Patch(string key, string value, HttpStatusCode status = HttpStatusCode.OK)
            {
                var response = await client.PatchAsJsonAsync($"/api/v1/admin/parametros/{key}", new { valor = value });
                Assert.Equal(status, response.StatusCode);
            }
            // Defaults sembrados y contrato de listado con módulo almacen.
            var listado = await client.GetStringAsync("/api/v1/admin/parametros?modulo=almacen");
            Assert.Contains(ParametrosUmbralesConteo.Nivel2, listado);
            Assert.Equal(new ConteoUmbrales(5, 1000, 1000, 10000), await provider.ObtenerAsync(default));

            async Task<Guid> Abrir(decimal monto)
            {
                var c = new ConteoInventario(Guid.NewGuid(), empresaId, TipoConteo.Rotativo,
                    new DateOnly(2026, 10, 8), actorId, subId);
                conteos.Add(c.Id);
                db.Conteos.Add(c);
                await db.SaveChangesAsync();
                await new IniciarConteoHandler(db, provider).Handle(new(c.Id), default);
                var linea = Assert.Single(c.Lineas);
                linea.Capturar(20000 + monto, actorId);
                c.EnviarAConciliacion();
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                return c.Id;
            }
            async Task Aprobar(Guid id, int nivel, HttpStatusCode esperado)
            {
                client.DefaultRequestHeaders.Remove("X-Test-Nivel");
                client.DefaultRequestHeaders.Add("X-Test-Nivel", nivel.ToString());
                var response = await client.PostAsync($"/api/v1/almacen/conteos/{id}/aprobar", null);
                Assert.Equal(esperado, response.StatusCode);
                if (esperado == HttpStatusCode.Forbidden)
                {
                    var problem = await response.Content.ReadAsStringAsync();
                    Assert.Contains("Nivel", problem);
                    Assert.Contains("MXN", problem);
                }
            }
            await Aprobar(await Abrir(800), 1, HttpStatusCode.NoContent);
            var mediano = await Abrir(5000);
            await Aprobar(mediano, 1, HttpStatusCode.Forbidden);
            await Aprobar(mediano, 2, HttpStatusCode.NoContent);
            var alto = await Abrir(12000);
            client.DefaultRequestHeaders.Remove("X-Test-Nivel");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/v1/almacen/conteos/{alto}/comparacion")).StatusCode);
            client.DefaultRequestHeaders.Add("X-Test-Nivel", "2");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/almacen/conteos/{alto}/comparacion")).StatusCode);
            await Aprobar(alto, 2, HttpStatusCode.Forbidden);
            await Aprobar(alto, 3, HttpStatusCode.NoContent);
            var entradas = await db.OutboxEntries.AsNoTracking().Where(e =>
                e.EventType == "almacen.conteo.ajuste_nivel3.aprobado.v1").ToListAsync();
            var entrada = Assert.Single(entradas, e => e.Payload.Contains(alto.ToString(), StringComparison.Ordinal));
            using var payload = JsonDocument.Parse(entrada.Payload);
            Assert.Equal(12000, payload.RootElement.GetProperty("MontoNetoMxn").GetDecimal());
            Assert.Equal(almacenId, payload.RootElement.GetProperty("AlmacenId").GetGuid());
            Assert.Equal(sucursalId, payload.RootElement.GetProperty("SucursalId").GetGuid());
            Assert.Equal(actorId, payload.RootElement.GetProperty("AprobadorId").GetGuid());
            Assert.Single(avisos.Items);

            var anterior = await Abrir(12000);
            var desde = DateTimeOffset.UtcNow.AddSeconds(-1);
            await Patch(ParametrosUmbralesConteo.Nivel2, "15000");
            await Aprobar(anterior, 2, HttpStatusCode.Forbidden);
            var nuevo = await Abrir(12000);
            await Aprobar(nuevo, 2, HttpStatusCode.NoContent);
            Assert.Equal(10000, (await db.Conteos.AsNoTracking().SingleAsync(c => c.Id == anterior)).UmbralNivel2Maximo);
            Assert.Equal(15000, (await db.Conteos.AsNoTracking().SingleAsync(c => c.Id == nuevo)).UmbralNivel2Maximo);
            Assert.Single(avisos.Items); // No hay aviso para el nuevo Nivel 2.
            var parametroId = await admin.ParametrosGlobales.Where(p => p.Clave == ParametrosUmbralesConteo.Nivel2)
                .Select(p => p.Id).SingleAsync();
            var auditoria = await admin.Set<AuditLogEntry>().AsNoTracking()
                .Where(a => a.EntidadId == parametroId && a.Timestamp >= desde && a.Operacion == "actualizar")
                .OrderByDescending(a => a.Timestamp).FirstAsync();
            Assert.Equal(actorId, auditoria.UsuarioId);
            Assert.False(string.IsNullOrWhiteSpace(auditoria.ActorNombre));
            using var cambios = JsonDocument.Parse(auditoria.Cambios);
            Assert.Equal("10000", cambios.RootElement.GetProperty("diff").GetProperty("Valor").GetProperty("antes").GetString());
            Assert.Equal("15000", cambios.RootElement.GetProperty("diff").GetProperty("Valor").GetProperty("despues").GetString());

            await Patch(ParametrosUmbralesConteo.Nivel1, "15000", HttpStatusCode.UnprocessableEntity);
            await Patch(ParametrosUmbralesConteo.Nivel1, "16000", HttpStatusCode.UnprocessableEntity);
            await Patch(ParametrosUmbralesConteo.Nivel2, "1000", HttpStatusCode.UnprocessableEntity);
            foreach (var key in ParametrosUmbralesConteo.Claves)
                await Patch(key, "-1", HttpStatusCode.UnprocessableEntity);
            await Patch(ParametrosUmbralesConteo.VariacionPct, "2.5");
            Assert.Equal(2.5m, (await provider.ObtenerAsync(default)).VariacionPctParaRecuento);
            Assert.Equal(1000m, (await provider.ObtenerAsync(default)).UmbralNivel1Maximo);

            // Ambos cambios serían válidos contra la foto anterior, pero no juntos.
            // El bloqueo de las cuatro filas debe permitir sólo uno.
            var concurrentes = await Task.WhenAll(
                client.PatchAsJsonAsync($"/api/v1/admin/parametros/{ParametrosUmbralesConteo.Nivel1}", new { valor = "12000" }),
                client.PatchAsJsonAsync($"/api/v1/admin/parametros/{ParametrosUmbralesConteo.Nivel2}", new { valor = "11000" }));
            Assert.Single(concurrentes, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(concurrentes, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            var final = await provider.ObtenerAsync(default);
            Assert.True(final.UmbralNivel1Maximo < final.UmbralNivel2Maximo);
        }
        finally
        {
            // Restaura la configuración aun si una aserción falla.
            admin.ChangeTracker.Clear();
            foreach (var row in await admin.ParametrosGlobales.Where(p => ParametrosUmbralesConteo.Claves.Contains(p.Clave)).ToListAsync())
                row.ActualizarValor(originales[row.Clave]);
            await admin.SaveChangesAsync();
            var eventos = await db.OutboxEntries.AsNoTracking().Where(e =>
                e.EventType == "almacen.conteo.ajuste_nivel3.aprobado.v1").ToListAsync();
            var eventoIds = eventos.Where(e => conteos.Any(id => e.Payload.Contains(id.ToString(), StringComparison.Ordinal)))
                .Select(e => e.Id).ToArray();
            await db.OutboxEntries.Where(e => eventoIds.Contains(e.Id)).ExecuteDeleteAsync();
            foreach (var id in conteos)
                await db.Conteos.Where(c => c.Id == id).ExecuteDeleteAsync();
            await db.SaldosInventario.Where(s => s.SubAlmacenId == subId).ExecuteDeleteAsync();
            await db.Ubicaciones.Where(u => u.SubAlmacenId == subId).ExecuteDeleteAsync();
            await db.SubAlmacenes.Where(s => s.Id == subId).ExecuteDeleteAsync();
            await db.Almacenes.Where(a => a.Id == almacenId).ExecuteDeleteAsync();
        }
    }

    private sealed class PermisosDelRequest(IHttpContextAccessor accessor) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permiso == $"almacen.inventarios.aprobar-nivel{accessor.HttpContext?.Request.Headers["X-Test-Nivel"]}");
    }
    private sealed class Avisos : INotificacionService
    {
        public List<AvisoAjusteNivel3> Items { get; } = [];
        public Task NotificarAjusteNivel3AprobadoAsync(AvisoAjusteNivel3 aviso, CancellationToken cancellationToken)
        {
            Items.Add(aviso);
            return Task.CompletedTask;
        }
        public Task NotificarValeSinRegularizarAsync(Guid id, string folio, Guid? persona,
            DateTimeOffset fecha, int dia, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
