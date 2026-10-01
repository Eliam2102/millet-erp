using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// ADM-07 D1/D2: sincronización de productos A+W y contrato V1 contra PostgreSQL real.
/// REQUIEREN Postgres (ConnectionStrings__Postgres); sin él el host no arranca. Productos es master
/// cross-empresa sin alcance por sucursal: la autorización es solo el permiso (no hay caso "otra sucursal").
/// </summary>
// El catálogo UnidadMedida sembrado no trae M2; el fixture usa PZA (sembrada) para que la unidad resuelva.
public class ProductosAwSincronizacionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/productos-aw";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private readonly WebApplicationFactory<Program> _factory;

    public ProductosAwSincronizacionEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string Json(string referencia, string descripcion) => $$$"""
        {"origen":{"vw_erp_articulo":[{"producto_ref":"{{{referencia}}}","descripcion":"{{{descripcion}}}","unidad_medida":"PZA",
         "baja":false,"variantes":[],"TRANSACTION_TIME":"2026-09-01T10:00:00"}]}}
        """;

    private WebApplicationFactory<Program> Host(string? archivo) => _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("IntegracionesAw:Productos:OrigenHabilitado", archivo is null ? "false" : "true");
        if (archivo is not null) b.UseSetting("IntegracionesAw:Productos:ArchivoSimulado", archivo);
    });

    private static string Archivo(string referencia, string descripcion)
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"prod-aw-{Guid.NewGuid():N}.json");
        File.WriteAllText(ruta, Json(referencia, descripcion));
        return ruta;
    }

    private static async Task<Guid> SembrarAsync(WebApplicationFactory<Program> f, string referencia, bool baja = false)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var p = new ProductoAw(Guid.CreateVersion7(), referencia, "DEMO", "M2");
        if (baja) p.DarDeBaja(DateTime.UtcNow);
        db.ProductosAw.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task Sin_permiso_gestionar_da_403_en_todos_los_endpoints()
    {
        await using var f = Host(null);
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesGestionar);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"{Base}/sincronizacion", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"{Base}/sincronizacion/DEMO-X", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Base}/{Guid.NewGuid()}/sincronizacion")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Base}/DEMO-X/contrato")).StatusCode);
    }

    [Fact]
    public async Task Post_sin_Idempotency_Key_da_400()
    {
        await using var f = Host(null);
        var conKey = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosProductosAwGestionar);
        var sinKey = f.CreateDefaultClient();
        sinKey.DefaultRequestHeaders.Authorization = conKey.DefaultRequestHeaders.Authorization;

        Assert.Equal(HttpStatusCode.BadRequest, (await sinKey.PostAsync($"{Base}/sincronizacion", null)).StatusCode);
    }

    [Fact]
    public async Task Origen_apagado_da_503_problem_details()
    {
        await using var f = Host(null);
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosProductosAwGestionar);

        var r = await c.PostAsync($"{Base}/sincronizacion", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("AW_PRODUCTOS_ORIGEN_DESHABILITADO", (await ReadJson(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Inexistentes_dan_404_sin_filtrar_datos()
    {
        await using var f = Host(Archivo("DEMO-OTRO", "X"));
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosProductosAwGestionar);

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{Base}/{Guid.NewGuid()}/sincronizacion")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{Base}/NO-EXISTE-{Guid.NewGuid():N}/contrato")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync($"{Base}/sincronizacion/NO-EXISTE", null)).StatusCode);
    }

    [Fact]
    public async Task Barrido_crea_y_el_estado_y_contrato_se_leen_con_ETag()
    {
        var referencia = $"DEMO-{Guid.NewGuid():N}"[..20];
        await using var f = Host(Archivo(referencia, "PRODUCTO DEMO"));
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosProductosAwGestionar);

        var barrido = await Json(await c.PostAsync($"{Base}/sincronizacion", null));
        Assert.True(barrido.GetProperty("creados").GetInt32() >= 1, barrido.ToString());

        var contrato = await c.GetAsync($"{Base}/{referencia}/contrato");
        var cj = await Json(contrato);
        Assert.Equal("1", cj.GetProperty("versionContrato").GetString());
        Assert.Equal("Activo", cj.GetProperty("estatus").GetString());
        Assert.NotNull(contrato.Headers.ETag);

        var estado = await Json(await c.GetAsync($"{Base}/{cj.GetProperty("id").GetGuid()}/sincronizacion"));
        Assert.Equal("Aplicado", estado.GetProperty("sincronizacion").GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task Reintento_con_If_Match_viejo_da_409_y_con_vigente_da_200()
    {
        var referencia = $"DEMO-{Guid.NewGuid():N}"[..20];
        await using var f = Host(Archivo(referencia, "NUEVA"));
        var id = await SembrarAsync(f, referencia);
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosProductosAwGestionar);
        var version = (await Json(await c.GetAsync($"{Base}/{id}/sincronizacion"))).GetProperty("version").GetInt32();

        var viejo = new HttpRequestMessage(HttpMethod.Post, $"{Base}/sincronizacion/{referencia}");
        viejo.Headers.TryAddWithoutValidation("If-Match", $"\"{version + 9}\"");
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(viejo)).StatusCode);

        var vigente = new HttpRequestMessage(HttpMethod.Post, $"{Base}/sincronizacion/{referencia}");
        vigente.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(vigente)).StatusCode);
    }

    [Fact]
    public async Task Contrato_devuelve_inactivo_con_estatus()
    {
        var referencia = $"DEMO-{Guid.NewGuid():N}"[..20];
        await using var f = Host(null);
        await SembrarAsync(f, referencia, baja: true);
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosProductosAwGestionar);

        var cj = await Json(await c.GetAsync($"{Base}/{referencia}/contrato"));

        Assert.Equal("Inactivo", cj.GetProperty("estatus").GetString());
    }

    // --- Helpers (duplicados a propósito, como en ClientesSincronizacionEndpointsTests) ---

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r)
    {
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return await ReadJson(r);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> f, string oid, string email)
    {
        var client = f.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        });
        var j = await Json(resp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<HttpClient> ClienteConPermisosAsync(WebApplicationFactory<Program> f, params string[] codigos)
    {
        var admin = await LoginAsync(f, "dev-superadmin", "superadmin@dev.local");
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"sync-test-{sufijo}";
        var email = $"sync-test-{sufijo}@test.local";

        var client = f.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-sync-{sufijo}", Nombre = $"Rol Sync {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(f, oid, email);
    }
}
