using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// U1.0: quitar un permiso a un rol o revocar el rol de un usuario surte
/// efecto en la siguiente petición (403), con la caché ya caliente y sin
/// esperar el TTL de 5 minutos.
/// </summary>
public class CambiosDeRolEfectoInmediatoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string UsuariosEndpoint = "/api/v1/identidad/usuarios";
    private const string RolesEndpoint = "/api/v1/identidad/roles";
    private static readonly Guid EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    // Ids deterministas del seed de PermisosCanonicos (Identidad).
    private static readonly Guid UsuariosLeer = Guid.Parse("00000002-0002-0000-0000-000000000001");
    private static readonly Guid RolesLeer = Guid.Parse("00000002-0002-0000-0000-000000000004");

    private readonly WebApplicationFactory<Program> _factory;

    public CambiosDeRolEfectoInmediatoTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Quitar_un_permiso_al_rol_da_403_a_sus_10_usuarios_en_la_siguiente_peticion()
    {
        var admin = await ClientDeAsync(SuperAdminOid);
        var rolId = await CrearRolAsync(admin, UsuariosLeer, RolesLeer);
        var clientes = new List<HttpClient>();
        for (var i = 0; i < 10; i++)
        {
            var u = await CrearUsuarioConRolAsync(admin, rolId);
            var client = await ClientDeAsync(u.Oid);
            // Calienta la caché de permisos de cada usuario.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(RolesEndpoint)).StatusCode);
            clientes.Add(client);
        }

        var put = await admin.PutAsJsonAsync($"{RolesEndpoint}/{rolId}/permisos", new { PermisoIds = new[] { UsuariosLeer } });
        put.EnsureSuccessStatusCode();

        foreach (var client in clientes)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(RolesEndpoint)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(UsuariosEndpoint)).StatusCode);
        }
    }

    [Fact]
    public async Task Revocar_el_rol_quita_el_acceso_de_inmediato()
    {
        var admin = await ClientDeAsync(SuperAdminOid);
        var rolId = await CrearRolAsync(admin, UsuariosLeer);
        var u = await CrearUsuarioConRolAsync(admin, rolId);
        var client = await ClientDeAsync(u.Oid);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(UsuariosEndpoint)).StatusCode);

        var del = await admin.DeleteAsync($"{UsuariosEndpoint}/asignaciones/{u.AsignacionId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(UsuariosEndpoint)).StatusCode);
    }

    private sealed record UsuarioPrueba(string Oid, Guid Id, Guid AsignacionId);

    private async Task<HttpClient> ClientDeAsync(string oid)
    {
        var client = _factory.CreateClientWithIdempotency();
        var (token, _) = await LoginAsync(oid, client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string Token, Guid UsuarioId)> LoginAsync(string oid, HttpClient? client = null)
    {
        client ??= _factory.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = $"{oid}@test.local",
            Nombre = oid,
            EmpresaId = (Guid?)null,
        });
        resp.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(resp);
        return (json.GetProperty("accessToken").GetString()!,
            json.GetProperty("usuario").GetProperty("id").GetGuid());
    }

    private static async Task<Guid> CrearRolAsync(HttpClient admin, params Guid[] permisoIds)
    {
        var created = await admin.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = $"u10-{Guid.NewGuid():N}"[..16],
            Nombre = "Rol U1.0 DEMO",
            Descripcion = (string?)null,
        });
        created.EnsureSuccessStatusCode();
        var rolId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var put = await admin.PutAsJsonAsync($"{RolesEndpoint}/{rolId}/permisos", new { PermisoIds = permisoIds });
        put.EnsureSuccessStatusCode();
        return rolId;
    }

    private async Task<UsuarioPrueba> CrearUsuarioConRolAsync(HttpClient admin, Guid rolId)
    {
        var oid = $"u10-{Guid.NewGuid():N}"[..20];
        var (_, usuarioId) = await LoginAsync(oid);
        var asignar = await admin.PostAsJsonAsync($"{UsuariosEndpoint}/{usuarioId}/asignaciones",
            new { EmpresaId, RolId = rolId });
        Assert.Equal(HttpStatusCode.Created, asignar.StatusCode);
        var asignacionId = (await ReadJsonAsync(asignar)).GetProperty("id").GetGuid();
        return new UsuarioPrueba(oid, usuarioId, asignacionId);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
