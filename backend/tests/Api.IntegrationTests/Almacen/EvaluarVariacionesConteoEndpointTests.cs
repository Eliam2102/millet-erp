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
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>PostgreSQL desechable. Verifica el contrato JSON camelCase de <c>evaluar-variaciones</c> (M-207).</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class EvaluarVariacionesConteoEndpointTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public async Task Evaluar_variaciones_responde_lineasMarcadasParaRecuento_camelCase()
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
        var clave = $"M207{Guid.NewGuid():N}"[..12];
        var conteoId = Guid.NewGuid();
        using (empresa.Bypass())
        {
            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                    VALUES ({almacenId}, {clave}, 'Prueba M-207', {sucursalId}, 0, 1, NOW(), NOW())
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                    VALUES ({subId}, {almacenId}, {clave}, 'Prueba M-207', 0, 0, 1, NOW(), NOW())
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                    VALUES ({rackId}, {subId}, 'M207', 'Prueba M-207', 0, false, 1, NOW(), NOW())
                    """);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO almacen.saldos_inventario
                        (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                    VALUES ({rackId}, {subId}, {articuloId}, 100, 1, NOW())
                    """);

                // Conteo iniciado por el flujo real: fija el snapshot de umbrales válido y pre-puebla líneas.
                var c = new ConteoInventario(conteoId, empresaId, TipoConteo.Rotativo,
                    new DateOnly(2026, 10, 8), actorId, subId);
                db.Conteos.Add(c);
                await db.SaveChangesAsync();
                await new IniciarConteoHandler(db, provider, articulos).Handle(new(conteoId), default);
                db.ChangeTracker.Clear();

                var response = await client.PostAsync($"/api/v1/almacen/conteos/{conteoId}/evaluar-variaciones", null);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = json.RootElement;
                Assert.True(root.TryGetProperty("lineasMarcadasParaRecuento", out var marcadas));
                Assert.Equal(JsonValueKind.Number, marcadas.ValueKind);
                Assert.Equal(0, marcadas.GetInt32());
                Assert.False(root.TryGetProperty("lineas_marcadas_para_recuento", out _));
            }
            finally
            {
                await db.Conteos.Where(x => x.Id == conteoId).ExecuteDeleteAsync();
                await db.SaldosInventario.Where(s => s.SubAlmacenId == subId).ExecuteDeleteAsync();
                await db.Ubicaciones.Where(u => u.SubAlmacenId == subId).ExecuteDeleteAsync();
                await db.SubAlmacenes.Where(s => s.Id == subId).ExecuteDeleteAsync();
                await db.Almacenes.Where(a => a.Id == almacenId).ExecuteDeleteAsync();
            }
        }
    }

    private sealed class PermisosCorporativos : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }
}
