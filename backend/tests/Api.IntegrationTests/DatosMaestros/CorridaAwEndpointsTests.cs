using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// ADM-06 R4: la corrida lanza clientes (encolado) y productos (síncrono) con el mismo id y el reporte de
/// diferencias sale en el JSON de ADR-0036. Comparte colección con las pruebas de clientes (cupo de un barrido vivo).
/// </summary>
[Collection("AwClientesSync")]
public sealed class CorridaAwEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/sincronizacion-aw/corridas";
    private static int _siguienteBase = Random.Shared.Next(1_000_000, 900_000_000);

    private sealed class OrigenFake(List<AwClienteOrigenFila> filas) : IAwClientesOrigen
    {
        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
            Task.FromResult(filas.FirstOrDefault(f => f.Id.ToString() == referencia));

        public Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct) =>
            Task.FromResult(new AwClientesPagina(cursor is null ? filas : [], null));
    }

    private static AwClienteOrigenFila Cliente(int id) => new(
        id, 1, $"CLIENTE DEMO {id}", null, null, "CALLE DEMO 1", "CIUDAD DEMO", "06600", "PROV", "MX",
        null, null, "5550001", null, null, "ZDEMO", "MXN", 1000m, 500m, 30d, null, 0, null, null,
        [new AwCondicionCoincidencia(30, 30)]);

    private WebApplicationFactory<Program> Host()
    {
        var baseId = Interlocked.Add(ref _siguienteBase, 10_000);
        var ruta = Path.Combine(Path.GetTempPath(), $"prod-aw-{Guid.NewGuid():N}.json");
        File.WriteAllText(ruta, $$$"""
            {"origen":{"vw_erp_articulo":[{"producto_ref":"CR{{{baseId}}}","descripcion":"PRODUCTO CORRIDA","unidad_medida":"PZA",
             "baja":false,"variantes":[],"TRANSACTION_TIME":"2026-09-01T10:00:00"}]}}
            """);
        return factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("IntegracionesAw:Clientes:LecturaHabilitada", "true");
            b.UseSetting("IntegracionesAw:Clientes:AplicacionHabilitada", "true");
            b.UseSetting("IntegracionesAw:Clientes:MapeoMoneda:MXN", "MXN");
            b.UseSetting("IntegracionesAw:Productos:OrigenHabilitado", "true");
            b.UseSetting("IntegracionesAw:Productos:ArchivoSimulado", ruta);
            b.ConfigureServices(s => s.AddSingleton<IAwClientesOrigen>(new OrigenFake([Cliente(baseId)])));
        });
    }

    private static async Task EsperarYLimpiarAsync(WebApplicationFactory<Program> f, Guid id)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
        var limite = DateTime.UtcNow.AddSeconds(40);
        while (await db.ClientesEjecuciones.AsNoTracking().AnyAsync(e => e.Id == id
            && (e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)))
        {
            if (DateTime.UtcNow > limite) break;
            await Task.Delay(300);
        }
        await db.ClientesEjecuciones.Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
    }

    [Fact]
    public async Task Post_con_ambos_permisos_da_202_y_guarda_el_resumen_de_productos_y_el_reporte_trae_las_11_columnas()
    {
        await using var f = Host();
        var http = await UsuariosAwHttp.ConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosProductosAwGestionar);

        var post = await http.PostAsync(Base, null);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var id = JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        Assert.Contains($"/clientes/sincronizacion/ejecuciones/{id}", post.Headers.Location!.ToString());
        try
        {
            using (var scope = f.Services.CreateScope())
            {
                var e = await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones.AsNoTracking().SingleAsync(x => x.Id == id);
                Assert.NotNull(e.ResumenProductos);
            }

            var reporte = await http.GetFromJsonAsync<JsonElement>($"{Base}/{id}/diferencias");
            Assert.Equal(11, reporte.GetProperty("columnas").GetArrayLength());
            Assert.True(reporte.GetProperty("totales").GetProperty("productosLeidos").GetDecimal() > 0, reporte.ToString());
            Assert.StartsWith("Diferencias de la corrida del", reporte.GetProperty("titulo").GetString());
            Assert.False(reporte.GetProperty("truncado").GetBoolean());
        }
        finally { await EsperarYLimpiarAsync(f, id); }
    }

    [Fact]
    public async Task Con_solo_clientes_sincronizar_el_post_da_403()
    {
        await using var f = Host();
        var http = await UsuariosAwHttp.ConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync(Base, null)).StatusCode);
    }

    [Fact]
    public async Task Corrida_inexistente_da_404()
    {
        await using var f = Host();
        var http = await UsuariosAwHttp.ConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{Base}/{Guid.NewGuid()}/diferencias")).StatusCode);
    }
}
