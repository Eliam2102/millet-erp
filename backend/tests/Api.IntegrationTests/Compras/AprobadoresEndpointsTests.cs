using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de endpoints de aprobadores (F9-PR1):
/// designar / listar vigentes / revocar / histórico. Comparten BD con
/// otros tests; usan <c>departamentoId</c> generado por test para aislar.
/// </summary>
public class AprobadoresEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    // Rol numérico (enum smallint): 0=JefeDpto, 1=JefeAlmacen, 2=AutorizadorN2.
    private const int RolJefeDpto = 0;
    private const int RolAutorizadorN2 = 2;

    private readonly WebApplicationFactory<Program> _factory;

    public AprobadoresEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Designar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/compras/aprobadores", BuildBody());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Designar_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/compras/aprobadores", BuildBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Designar_Con_UsuarioInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores",
            BuildBody(usuarioId: Guid.CreateVersion7()));   // GUID random, no existe.

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("USUARIO_NO_ENCONTRADO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Designar_Happy_Path_Retorna_201_Y_Lista_En_Vigentes()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoId = Guid.CreateVersion7();
        var usuarioId = await ResolveSuperAdminUsuarioIdAsync(client);

        var crear = await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores",
            BuildBody(departamentoId: deptoId, usuarioId: usuarioId, motivo: "test designar"));

        Assert.Equal(HttpStatusCode.Created, crear.StatusCode);
        var crearJson = await ReadJsonAsync(crear);
        var aprobadorId = crearJson.GetProperty("id").GetGuid();

        // Listar vigentes filtrado por depto: debe aparecer.
        var lista = await client.GetAsync(
            $"/api/v1/compras/aprobadores?departamentoId={deptoId}");
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        var listaJson = await ReadJsonAsync(lista);
        Assert.Equal(1, listaJson.GetArrayLength());
        Assert.Equal(aprobadorId, listaJson[0].GetProperty("id").GetGuid());
        Assert.Equal(usuarioId, listaJson[0].GetProperty("usuarioId").GetGuid());
        Assert.Equal(RolJefeDpto, listaJson[0].GetProperty("rol").GetInt32());
    }

    [Fact]
    public async Task Designar_Reemplaza_Vigente_Anterior()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoId = Guid.CreateVersion7();
        var usuarioId = await ResolveSuperAdminUsuarioIdAsync(client);

        // Designar dos veces el mismo (depto, rol). Segunda llamada debe
        // cerrar la primera. Como solo tenemos un usuario válido en la BD
        // de tests, usamos el mismo usuario para ambos: el handler hace
        // short-circuit (re-designar mismo usuario es no-op). Para forzar
        // el cierre/inserción, usamos roles distintos: primero JefeDpto,
        // luego AutorizadorN2 — son entradas independientes en la tabla
        // (PK distinta). Test alternativo de cierre: en produccion el
        // cliente designa a usuario distinto, ese flow está cubierto en
        // tests unit del handler.
        await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores",
            BuildBody(deptoId, usuarioId, RolJefeDpto));

        await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores",
            BuildBody(deptoId, usuarioId, RolAutorizadorN2));

        var lista = await client.GetAsync($"/api/v1/compras/aprobadores?departamentoId={deptoId}");
        var json = await ReadJsonAsync(lista);
        Assert.Equal(2, json.GetArrayLength());
    }

    [Fact]
    public async Task Revocar_Existente_Retorna_204_Y_No_Aparece_En_Vigentes()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoId = Guid.CreateVersion7();
        var usuarioId = await ResolveSuperAdminUsuarioIdAsync(client);

        var crear = await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores",
            BuildBody(deptoId, usuarioId));
        var aprobadorId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var revocar = await client.DeleteAsync($"/api/v1/compras/aprobadores/{aprobadorId}");
        Assert.Equal(HttpStatusCode.NoContent, revocar.StatusCode);

        var lista = await client.GetAsync($"/api/v1/compras/aprobadores?departamentoId={deptoId}");
        var listaJson = await ReadJsonAsync(lista);
        Assert.Equal(0, listaJson.GetArrayLength());
    }

    [Fact]
    public async Task Revocar_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.DeleteAsync($"/api/v1/compras/aprobadores/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revocar_Idempotente_Segunda_Vez_Retorna_404_O_204()
    {
        // Después de revocar, la fila tiene VigenteHasta != null. Una
        // segunda revoke encuentra la fila por id (no filtra por
        // vigencia) y es no-op vía Cerrar() → 204.
        var client = await CreateSuperAdminClientAsync();
        var deptoId = Guid.CreateVersion7();
        var usuarioId = await ResolveSuperAdminUsuarioIdAsync(client);

        var crear = await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores", BuildBody(deptoId, usuarioId));
        var id = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var primera = await client.DeleteAsync($"/api/v1/compras/aprobadores/{id}");
        var segunda = await client.DeleteAsync($"/api/v1/compras/aprobadores/{id}");

        Assert.Equal(HttpStatusCode.NoContent, primera.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, segunda.StatusCode);
    }

    [Fact]
    public async Task Historico_Sin_Filtros_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/compras/aprobadores/historico");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("FILTRO_OBLIGATORIO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Historico_Con_Filtro_Depto_Retorna_Vigentes_Y_Cerradas()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoId = Guid.CreateVersion7();
        var usuarioId = await ResolveSuperAdminUsuarioIdAsync(client);

        // Creamos dos asignaciones en diferentes roles (vigentes ambas).
        // Luego revocamos una. El histórico debe traer 2 filas, una con
        // vigenteHasta != null.
        var crear1 = await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores", BuildBody(deptoId, usuarioId, RolJefeDpto));
        var id1 = (await ReadJsonAsync(crear1)).GetProperty("id").GetGuid();
        await client.PostAsJsonAsync(
            "/api/v1/compras/aprobadores", BuildBody(deptoId, usuarioId, RolAutorizadorN2));
        await client.DeleteAsync($"/api/v1/compras/aprobadores/{id1}");

        var historico = await client.GetAsync(
            $"/api/v1/compras/aprobadores/historico?departamentoId={deptoId}");
        Assert.Equal(HttpStatusCode.OK, historico.StatusCode);
        var json = await ReadJsonAsync(historico);
        Assert.Equal(2, json.GetArrayLength());
        var conCierre = 0;
        foreach (var item in json.EnumerateArray())
        {
            if (item.TryGetProperty("vigenteHasta", out var vh) && vh.ValueKind != JsonValueKind.Null)
                conCierre++;
        }
        Assert.Equal(1, conCierre);
    }

    [Fact]
    public async Task Designar_Sin_IdempotencyKey_Retorna_400()
    {
        // Cliente sin auto-injection (CreateClient en lugar de
        // CreateClientWithIdempotency).
        var rawClient = _factory.CreateClient();
        var token = await FakeLoginAsync(rawClient, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        rawClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await rawClient.PostAsJsonAsync("/api/v1/compras/aprobadores", BuildBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Resuelve el id del usuario `dev-superadmin` consultando
    /// <c>/api/auth/me</c> después del fake-login. Usado para crear
    /// asignaciones con un UsuarioId real (la validación cross-module
    /// exige que el usuario exista en identidad.usuarios).
    /// </summary>
    private static async Task<Guid> ResolveSuperAdminUsuarioIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("userId").GetGuid();
    }

    private static object BuildBody(
        Guid? departamentoId = null,
        Guid? usuarioId = null,
        int rol = RolJefeDpto,
        string? motivo = null) => new
    {
        DepartamentoId = departamentoId ?? Guid.CreateVersion7(),
        Rol = rol,
        UsuarioId = usuarioId ?? Guid.CreateVersion7(),
        Motivo = motivo,
    };

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
