using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>Catálogo de clasificaciones de cliente: duplicado, validación, concurrencia, permisos, idempotencia y seed DEMO.</summary>
public sealed class ClasificacionesClienteEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/clasificaciones-cliente";
    private const string SeedConstructora = "00000012-0001-0000-0000-000000000001";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private async Task<HttpClient> SuperAdminAsync() => await LoginAsync("dev-superadmin", "superadmin@dev.local");

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private async Task<HttpClient> LoginAsync(string oid, string email)
    {
        var client = factory.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        });
        resp.EnsureSuccessStatusCode();
        var j = await Json(resp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<HttpClient> ClienteConPermisosAsync(params string[] codigos)
    {
        var admin = await LoginAsync("dev-superadmin", "superadmin@dev.local");
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"orig-test-{sufijo}";
        var email = $"orig-test-{sufijo}@test.local";

        var client = factory.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-cla-{sufijo}", Nombre = $"Rol Cla {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(oid, email);
    }

    // Nombre único con prefijo F1A- y longitud exacta n (máx. 100 según validador).
    private static string NombreDeLongitud(int n)
    {
        var prefijo = "F1A-" + Guid.NewGuid().ToString("N")[..8] + "-";
        return prefijo + new string('X', n - prefijo.Length);
    }

    private async Task BorrarAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        db.RemoveRange(await db.Set<ClasificacionCliente>().IgnoreQueryFilters().Where(c => ids.Contains(c.Id)).ToListAsync());
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Nombre_duplicado_sin_distinguir_mayusculas_es_422()
    {
        using var c = await SuperAdminAsync();
        foreach (var nombre in new[] { "  CONSTRUCTORA  ", "constructora" })
        {
            var r = await c.PostAsJsonAsync(Base, new { nombre, descripcion = "duplicada", activa = true });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Contains("CLASIFICACION_DUPLICADA", await r.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Altas_simultaneas_del_mismo_nombre_dan_un_201_y_un_422_nunca_500()
    {
        using var c = await SuperAdminAsync();
        var nombre = "F1A-" + Guid.NewGuid().ToString("N")[..8];
        var ids = new List<Guid>();
        try
        {
            var envios = Enumerable.Range(0, 2)
                .Select(_ => c.PostAsJsonAsync(Base, new { nombre, descripcion = "carrera", activa = true }))
                .ToArray();
            var respuestas = await Task.WhenAll(envios);

            Assert.DoesNotContain(respuestas, r => r.StatusCode == HttpStatusCode.InternalServerError);
            Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.OK);
            var rechazada = Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            Assert.Contains("CLASIFICACION_DUPLICADA", await rechazada.Content.ReadAsStringAsync());

            foreach (var r in respuestas.Where(r => r.StatusCode == HttpStatusCode.OK))
                ids.Add((await Json(r)).GetProperty("id").GetGuid());
        }
        finally { await BorrarAsync(ids); }
    }

    [Fact]
    public async Task Validacion_de_nombre_solo_espacios_es_400()
    {
        using var c = await SuperAdminAsync();
        var r = await c.PostAsJsonAsync(Base, new { nombre = "   ", descripcion = "x", activa = true });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Theory]
    [InlineData(0, 10, 400)]     // nombre vacío
    [InlineData(101, 10, 400)]   // nombre de 101 caracteres
    [InlineData(100, 501, 400)]  // descripción de 501 caracteres
    [InlineData(100, 500, 200)]  // límites válidos
    public async Task Validacion_de_nombre_y_descripcion(int nombreLen, int descLen, int esperado)
    {
        using var c = await SuperAdminAsync();
        var nombre = nombreLen == 0 ? string.Empty : NombreDeLongitud(nombreLen);
        var ids = new List<Guid>();
        try
        {
            var r = await c.PostAsJsonAsync(Base, new { nombre, descripcion = new string('d', descLen), activa = true });
            Assert.Equal(esperado, (int)r.StatusCode);
            if (esperado == 200) ids.Add((await Json(r)).GetProperty("id").GetGuid());
        }
        finally { await BorrarAsync(ids); }
    }

    [Fact]
    public async Task Version_vieja_es_409_CLASIFICACION_CAMBIO()
    {
        using var c = await SuperAdminAsync();
        var nombre = NombreDeLongitud(30);
        var ids = new List<Guid>();
        try
        {
            var alta = await Json(await c.PostAsJsonAsync(Base, new { nombre, descripcion = "v1", activa = true }));
            var id = alta.GetProperty("id").GetGuid(); ids.Add(id);
            var lista = await Json(await c.GetAsync(Base));
            var version = lista.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id).GetProperty("version").GetInt32();

            var ok = await c.PutAsJsonAsync($"{Base}/{id}", new { nombre, descripcion = "v2", activa = true, version });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

            var vieja = await c.PutAsJsonAsync($"{Base}/{id}", new { nombre, descripcion = "v3", activa = true, version });
            Assert.Equal(HttpStatusCode.Conflict, vieja.StatusCode);
            Assert.Contains("CLASIFICACION_CAMBIO", await vieja.Content.ReadAsStringAsync());
        }
        finally { await BorrarAsync(ids); }
    }

    [Fact]
    public async Task Sin_gestionar_es_403_en_get_post_put()
    {
        using var c = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesOrigenVer);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Base, new { nombre = "NO-PERMITIDO", descripcion = "x", activa = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PutAsJsonAsync($"{Base}/{SeedConstructora}", new { nombre = "Constructora", descripcion = "x", activa = true, version = 1 })).StatusCode);
    }

    [Fact]
    public async Task Post_sin_idempotency_key_es_400()
    {
        // Cliente sin el handler de Idempotency-Key: login manual y POST desnudo.
        using var c = factory.CreateClient();
        var login = await Json(await c.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = EmpresaBootstrapId,
        }));
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());

        var r = await c.PostAsJsonAsync(Base, new { nombre = NombreDeLongitud(30), descripcion = "x", activa = true });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Mismo_POST_con_la_misma_Idempotency_Key_repite_la_respuesta_y_no_duplica_la_fila()
    {
        // M-92: el middleware responde replay (mismo status/cuerpo + Idempotent-Replayed) cuando
        // la clave y el cuerpo coinciden; la fila se crea una sola vez.
        using var c = await SuperAdminAsync();
        var nombre = NombreDeLongitud(30);
        var body = new { nombre, descripcion = "idempotente", activa = true };
        var key = Guid.NewGuid().ToString("D");
        var ids = new List<Guid>();
        try
        {
            using var primeraReq = new HttpRequestMessage(HttpMethod.Post, Base) { Content = JsonContent.Create(body) };
            primeraReq.Headers.Add("Idempotency-Key", key);
            var primera = await c.SendAsync(primeraReq);
            using var segundaReq = new HttpRequestMessage(HttpMethod.Post, Base) { Content = JsonContent.Create(body) };
            segundaReq.Headers.Add("Idempotency-Key", key);
            var segunda = await c.SendAsync(segundaReq);

            var idPrimera = (await Json(primera)).GetProperty("id").GetGuid();
            ids.Add(idPrimera);
            Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
            Assert.True(segunda.Headers.Contains("Idempotent-Replayed"));
            Assert.Equal(idPrimera, (await Json(segunda)).GetProperty("id").GetGuid());

            var lista = await Json(await c.GetAsync(Base));
            Assert.Equal(1, lista.EnumerateArray().Count(x => x.GetProperty("nombre").GetString() == nombre));
        }
        finally { await BorrarAsync(ids); }
    }

    [Fact]
    public async Task Seed_demo_tiene_cinco_activas()
    {
        using var c = await SuperAdminAsync();
        var lista = await Json(await c.GetAsync(Base));
        var esperados = new Dictionary<string, string>
        {
            ["00000012-0001-0000-0000-000000000001"] = "Constructora",
            ["00000012-0001-0000-0000-000000000002"] = "Cancelero",
            ["00000012-0001-0000-0000-000000000003"] = "Particular",
            ["00000012-0001-0000-0000-000000000004"] = "Distribuidor",
            ["00000012-0001-0000-0000-000000000005"] = "Gobierno",
        };
        foreach (var (id, nombre) in esperados)
        {
            var item = lista.EnumerateArray().Single(x => x.GetProperty("id").GetGuid().ToString() == id);
            Assert.Equal(nombre, item.GetProperty("nombre").GetString());
            Assert.True(item.GetProperty("activa").GetBoolean());
        }
        var activasSeed = lista.EnumerateArray().Count(x => x.GetProperty("id").GetGuid().ToString().StartsWith("00000012-0001-", StringComparison.Ordinal) && x.GetProperty("activa").GetBoolean());
        Assert.Equal(5, activasSeed);
    }
}
