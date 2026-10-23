using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Identidad.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
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

    private sealed class ProductosOrigenQueFalla : IAwProductosOrigen
    {
        public Task<AwProductoOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) => throw new InvalidOperationException("origen roto");
        public Task<AwProductosPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct) => throw new InvalidOperationException("origen roto");
    }

    private WebApplicationFactory<Program> Host(bool productosFallan = false) => Host(out _, productosFallan);

    private WebApplicationFactory<Program> Host(out int baseId, bool productosFallan = false)
    {
        baseId = Interlocked.Add(ref _siguienteBase, 10_000);
        var cliente = baseId;
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
            b.ConfigureServices(s => {
                s.AddSingleton<IAwClientesOrigen>(new OrigenFake([Cliente(cliente)]));
                if (productosFallan) s.AddSingleton<IAwProductosOrigen>(new ProductosOrigenQueFalla());
            });
        });
    }

    /// <summary>Espera a que la ejecución encolada de clientes termine (hasta 40 s); devuelve si terminó.</summary>
    private static async Task<bool> EsperarFinAsync(WebApplicationFactory<Program> f, Guid id)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
        var limite = DateTime.UtcNow.AddSeconds(40);
        while (await db.ClientesEjecuciones.AsNoTracking().AnyAsync(e => e.Id == id
            && (e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)))
        {
            if (DateTime.UtcNow > limite) return false;
            await Task.Delay(300);
        }
        return true;
    }

    private static async Task EsperarYLimpiarAsync(WebApplicationFactory<Program> f, Guid id)
    {
        await EsperarFinAsync(f, id);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>();
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

    private static async Task<Guid> PostAsync(HttpClient http)
    {
        var post = await http.PostAsync(Base, null);
        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        return JsonDocument.Parse(await post.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<int> FilasBitacoraAsync(WebApplicationFactory<Program> f, string referencia)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().CambiosMaestrosAw.AsNoTracking()
            .CountAsync(c => c.ReferenciaExterna == referencia);
    }

    [Fact]
    public async Task Corrida_completa_deja_totales_de_clientes_y_una_segunda_no_crea_ni_agrega_filas_de_bitacora()
    {
        await using var f = Host(out var baseId);
        var http = await UsuariosAwHttp.ConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosProductosAwGestionar);
        var referencia = baseId.ToString();

        var id1 = await PostAsync(http);
        Guid? id2 = null;
        try
        {
            Assert.True(await EsperarFinAsync(f, id1), "La corrida de clientes no terminó a tiempo.");
            var r1 = await http.GetFromJsonAsync<JsonElement>($"{Base}/{id1}/diferencias");
            var t1 = r1.GetProperty("totales");
            Assert.Equal(1, t1.GetProperty("clientesLeidos").GetDecimal());
            Assert.Equal(1, t1.GetProperty("clientesCreados").GetDecimal());
            Assert.Equal(0, t1.GetProperty("clientesErrores").GetDecimal());
            Assert.Equal(1, t1.GetProperty("productosCreados").GetDecimal());
            var filasBitacora = await FilasBitacoraAsync(f, referencia);

            id2 = await PostAsync(http);
            Assert.True(await EsperarFinAsync(f, id2.Value), "La segunda corrida de clientes no terminó a tiempo.");
            var t2 = (await http.GetFromJsonAsync<JsonElement>($"{Base}/{id2}/diferencias")).GetProperty("totales");
            Assert.Equal(1, t2.GetProperty("clientesLeidos").GetDecimal());
            Assert.Equal(0, t2.GetProperty("clientesCreados").GetDecimal());
            Assert.Equal(1, t2.GetProperty("clientesSinCambios").GetDecimal());
            Assert.Equal(0, t2.GetProperty("productosCreados").GetDecimal());
            Assert.Equal(filasBitacora, await FilasBitacoraAsync(f, referencia));
        }
        finally
        {
            await EsperarYLimpiarAsync(f, id1);
            if (id2 is { } segunda) await EsperarYLimpiarAsync(f, segunda);
        }
    }

    [Fact]
    public async Task Si_el_barrido_de_productos_falla_el_post_sigue_dando_202_y_el_error_queda_en_el_resumen()
    {
        await using var f = Host(productosFallan: true);
        var http = await UsuariosAwHttp.ConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosProductosAwGestionar);
        var id = await PostAsync(http);
        try
        {
            using var scope = f.Services.CreateScope();
            var e = await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones.AsNoTracking().SingleAsync(x => x.Id == id);
            using var resumen = JsonDocument.Parse(e.ResumenProductos!);
            Assert.Equal("barrido_productos_fallido", resumen.RootElement.GetProperty("error").GetString());
            Assert.DoesNotContain("origen roto", e.ResumenProductos, StringComparison.Ordinal);
            var reporte = await http.GetFromJsonAsync<JsonElement>($"{Base}/{id}/diferencias");
            Assert.Equal(0, reporte.GetProperty("totales").GetProperty("productosLeidos").GetDecimal());
        }
        finally { await EsperarYLimpiarAsync(f, id); }
    }

    [Fact]
    public async Task Diferencias_sin_permiso_de_productos_no_incluye_filas_ni_totales_de_productos()
    {
        await using var f = Host(out var baseId);
        var completo = await UsuariosAwHttp.ConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosProductosAwGestionar);
        var id = await PostAsync(completo);
        try
        {
            Assert.True(await EsperarFinAsync(f, id));
            var soloClientes = await UsuariosAwHttp.ConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);
            var reporte = await soloClientes.GetFromJsonAsync<JsonElement>($"{Base}/{id}/diferencias");
            Assert.Equal(0, reporte.GetProperty("totales").GetProperty("productosLeidos").GetDecimal());
            Assert.DoesNotContain(reporte.GetProperty("filas").EnumerateArray(), x => x.GetProperty("entidad").GetString() == "ProductoAw");
            var conProductos = await completo.GetFromJsonAsync<JsonElement>($"{Base}/{id}/diferencias");
            Assert.True(conProductos.GetProperty("totales").GetProperty("productosLeidos").GetDecimal() > 0);
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
