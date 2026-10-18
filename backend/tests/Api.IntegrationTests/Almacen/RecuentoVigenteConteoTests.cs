using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// A7: la cantidad vigente de una línea es la del último recuento (mayor secuencia).
/// Verifica comparación (HTTP), y que aplicar ajusta el inventario contra la vigente
/// y no contra la captura original. PostgreSQL desechable; fixture aislado por GUIDs.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public sealed class RecuentoVigenteConteoTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Ultimo_recuento_es_la_cantidad_vigente_y_aplicar_ajusta_contra_ella()
    {
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            // Alcance corporativo concedido: el almacén de prueba no tiene sucursal asociada al usuario dev.
            s.RemoveAll<ICurrentUserPermissions>();
            s.AddScoped<ICurrentUserPermissions, PermisosCorporativos>();
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
        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        var empresaId = me.RootElement.GetProperty("currentEmpresaId").GetGuid();
        var actorId = me.RootElement.GetProperty("userId").GetGuid();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var empresa = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IConteoUmbralesProvider>();
        var articulos = scope.ServiceProvider.GetRequiredService<IArticuloReadPort>();
        var almacenId = Guid.NewGuid(); var subId = Guid.NewGuid(); var rackId = Guid.NewGuid();
        var articuloId = Guid.NewGuid(); var sucursalId = Guid.NewGuid();
        var clave = $"A7{Guid.NewGuid():N}"[..12];
        var conteoId = Guid.NewGuid();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        using (empresa.Bypass())
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                    VALUES ({almacenId}, {clave}, 'Prueba A7 vigente', {sucursalId}, 0, 1, NOW(), NOW())
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                    VALUES ({subId}, {almacenId}, {clave}, 'Prueba A7 vigente', 0, 0, 1, NOW(), NOW())
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                    VALUES ({rackId}, {subId}, 'A7V', 'Prueba A7 vigente', 0, false, 1, NOW(), NOW())
                    """);
                // Teórica 100 a costo 1: 1 unidad = 1 MXN, muy por debajo del umbral de 1000 MXN.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.saldos_inventario
                        (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                    VALUES ({rackId}, {subId}, {articuloId}, 100, 1, NOW())
                    """);

                var c = new ConteoInventario(conteoId, empresaId, TipoConteo.Rotativo, hoy, actorId, subId);
                db.Conteos.Add(c);
                await db.SaveChangesAsync();
                await new IniciarConteoHandler(db, provider, articulos).Handle(new(conteoId), default);

                // Captura original: 4 (variación -96, sobre el umbral del 5%).
                var conteoTracked = await db.Conteos.Include(x => x.Lineas).FirstAsync(x => x.Id == conteoId);
                var linea = conteoTracked.Lineas.Single();
                var lineaId = linea.Id;
                linea.Capturar(4m, actorId);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                var evaluar = await client.PostAsync($"/api/v1/almacen/conteos/{conteoId}/evaluar-variaciones", null);
                Assert.Equal(HttpStatusCode.OK, evaluar.StatusCode);
                using (var marcas = JsonDocument.Parse(await evaluar.Content.ReadAsStringAsync()))
                    Assert.Equal(1, marcas.RootElement.GetProperty("lineasMarcadasParaRecuento").GetInt32());

                // Primer recuento (secuencia 1) = 99.
                var r1 = await client.PostAsJsonAsync($"/api/v1/almacen/conteos/{conteoId}/lineas/{lineaId}/recuento",
                    new { cantidadRecontada = 99m });
                Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
                using (var j1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()))
                    Assert.Equal(1, j1.RootElement.GetProperty("secuencia").GetInt32());

                var comp1 = await LeerLineaAsync(client, conteoId, lineaId);
                Assert.Equal(4m, comp1.GetProperty("cantidadRealCapturada").GetDecimal());
                Assert.Equal(99m, comp1.GetProperty("cantidadVigente").GetDecimal());
                Assert.True(comp1.GetProperty("recuentoRealizado").GetBoolean());
                Assert.True(comp1.GetProperty("requiereRecuento").GetBoolean()); // historial: no se apaga
                Assert.Equal(-1m, comp1.GetProperty("variacionAbsoluta").GetDecimal());
                Assert.Equal(-1m, comp1.GetProperty("variacionValorMxn").GetDecimal());

                // Segundo recuento (secuencia 2) = 98: prevalece sobre el 1.
                var r2 = await client.PostAsJsonAsync($"/api/v1/almacen/conteos/{conteoId}/lineas/{lineaId}/recuento",
                    new { cantidadRecontada = 98m });
                Assert.Equal(HttpStatusCode.Created, r2.StatusCode);
                using (var j2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync()))
                    Assert.Equal(2, j2.RootElement.GetProperty("secuencia").GetInt32());

                var comp2 = await LeerLineaAsync(client, conteoId, lineaId);
                Assert.Equal(4m, comp2.GetProperty("cantidadRealCapturada").GetDecimal());
                Assert.Equal(98m, comp2.GetProperty("cantidadVigente").GetDecimal());
                Assert.Equal(-2m, comp2.GetProperty("variacionAbsoluta").GetDecimal());

                // Aprobación (dominio) y aplicación con la cantidad vigente 98: ajuste de -2, no de -96.
                var paraAprobar = await db.Conteos.Include(x => x.Lineas).FirstAsync(x => x.Id == conteoId);
                paraAprobar.EnviarAConciliacion();
                paraAprobar.Aprobar(actorId);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();

                // Calendario de una empresa propia: el ejercicio del año de la empresa real lo crean/reutilizan otras pruebas (ux_ejercicios_contables_anio).
                var calendario = new PeriodoContableFixture(db.Database.GetConnectionString()!, Guid.NewGuid(), hoy.Year);
                await using (calendario)
                {
                    await calendario.SembrarAsync(hoy.Month);
                    var resp = await new AplicarConteoHandler(
                        db, new NoOpEvents(), new FakeUserCtx(actorId), new FakeEmpresaCtx(empresaId), calendario.Port)
                        .Handle(new AplicarConteoCommand(conteoId), CancellationToken.None);
                    Assert.Equal(1, resp.MovimientosGenerados);
                    Assert.Equal(-2m, resp.MontoNetoMxn);
                }

                var movimiento = await db.Set<MovimientoInventario>().AsNoTracking()
                    .IgnoreQueryFilters()
                    .Include(m => m.Lineas)
                    .SingleAsync(m => m.ConteoId == conteoId);
                Assert.Equal(TipoMovimiento.AjusteNegativo, movimiento.Tipo);
                Assert.Equal(2m, Assert.Single(movimiento.Lineas).Cantidad);

                var saldo = await db.SaldosInventario.AsNoTracking()
                    .FirstAsync(s => s.UbicacionId == rackId && s.ArticuloId == articuloId);
                Assert.Equal(98m, saldo.Cantidad);
            }
            finally
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.movimientos_inventario WHERE conteo_id = {conteoId}");
                await db.Database.ExecuteSqlInterpolatedAsync($@"DELETE FROM almacen.recuentos_conteo WHERE linea_conteo_id IN (
                    SELECT id FROM almacen.lineas_conteo WHERE conteo_id = {conteoId})");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.lineas_conteo WHERE conteo_id = {conteoId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.conteos_inventario WHERE id = {conteoId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {rackId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {rackId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
            }
        }
    }

    private static async Task<JsonElement> LeerLineaAsync(HttpClient client, Guid conteoId, Guid lineaId)
    {
        var response = await client.GetAsync($"/api/v1/almacen/conteos/{conteoId}/comparacion");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fila = json.RootElement.EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == lineaId);
        return fila.Clone();
    }

    private sealed class PermisosCorporativos : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class NoOpEvents : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeUserCtx(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? UserName => "Test A7 vigente";
    }

    private sealed class FakeEmpresaCtx(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
