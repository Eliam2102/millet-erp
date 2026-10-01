using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Permisos personalizados por usuario (ADR-0053): dos usuarios con el MISMO
/// rol obtienen permisos efectivos distintos; las excepciones aplican de
/// inmediato (sin esperar el TTL de la caché); reglas de seguridad y borrado
/// por cambio de rol.
/// </summary>
public class PermisosPersonalizadosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string UsuariosEndpoint = "/api/v1/identidad/usuarios";
    private const string RolesEndpoint = "/api/v1/identidad/roles";
    private static readonly Guid EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    // Ids deterministas del seed de PermisosCanonicos (Identidad).
    private static readonly Guid UsuariosLeer = Guid.Parse("00000002-0002-0000-0000-000000000001");
    private static readonly Guid UsuariosCrear = Guid.Parse("00000002-0002-0000-0000-000000000002");
    private static readonly Guid RolesLeer = Guid.Parse("00000002-0002-0000-0000-000000000004");
    private static readonly Guid GestionarPermisos = Guid.Parse("00000002-0002-0000-0000-00000000000e");

    // Rol de sistema NO super-admin sembrado por el bootstrap (admin-catalogos).
    private static readonly Guid RolAdminCatalogos = Guid.Parse("00000002-0003-0000-0000-000000000004");

    private readonly WebApplicationFactory<Program> _factory;

    public PermisosPersonalizadosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dos_usuarios_con_el_mismo_rol_y_overrides_distintos_tienen_permisos_distintos()
    {
        var admin = await SuperAdminAsync();
        var rolId = await CrearRolAsync(admin, UsuariosLeer, RolesLeer);
        var u1 = await CrearUsuarioConRolAsync(admin, rolId);
        var u2 = await CrearUsuarioConRolAsync(admin, rolId);

        await PutOverridesAsync(admin, u1.Id, new[] { Item(RolesLeer, "Denegar") });
        await PutOverridesAsync(admin, u2.Id, new[] { Item(UsuariosCrear, "Conceder") });

        var p1 = (await LoginAsync(u1.Oid)).Permisos;
        var p2 = (await LoginAsync(u2.Oid)).Permisos;
        Assert.Contains("identidad.usuarios.leer", p1);
        Assert.DoesNotContain("identidad.roles.leer", p1);
        Assert.Contains("identidad.roles.leer", p2);
        Assert.Contains("identidad.usuarios.crear", p2);
        Assert.DoesNotContain("identidad.usuarios.crear", p1);
    }

    [Fact]
    public async Task Denegar_da_403_y_conceder_da_200_de_inmediato_sin_esperar_el_TTL()
    {
        var admin = await SuperAdminAsync();
        var rolId = await CrearRolAsync(admin, UsuariosLeer);
        var u = await CrearUsuarioConRolAsync(admin, rolId);
        var userClient = await ClientDeAsync(u.Oid);

        // Calienta la caché: el rol trae usuarios.leer, no roles.leer.
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync(UsuariosEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync(RolesEndpoint)).StatusCode);

        // Denegar un permiso que el rol sí trae.
        await PutOverridesAsync(admin, u.Id, new[] { Item(UsuariosLeer, "Denegar") });
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync(UsuariosEndpoint)).StatusCode);

        // Conceder uno que el rol no trae.
        await PutOverridesAsync(admin, u.Id, new[] { Item(RolesLeer, "Conceder") });
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync(RolesEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync(UsuariosEndpoint)).StatusCode);

        // Restablecer vuelve al rol.
        var del = await admin.DeleteAsync($"{UsuariosEndpoint}/{u.Id}/empresas/{EmpresaId}/permisos-override");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync(RolesEndpoint)).StatusCode);
    }

    [Fact]
    public async Task Get_permisos_efectivos_muestra_origen_Rol_Concedido_Denegado()
    {
        var admin = await SuperAdminAsync();
        var rolId = await CrearRolAsync(admin, UsuariosLeer, RolesLeer);
        var u = await CrearUsuarioConRolAsync(admin, rolId);
        await PutOverridesAsync(admin, u.Id, new[]
        {
            Item(RolesLeer, "Denegar", "motivo de prueba"),
            Item(UsuariosCrear, "Conceder"),
        });

        var resp = await admin.GetAsync($"{UsuariosEndpoint}/{u.Id}/empresas/{EmpresaId}/permisos");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await ReadJsonAsync(resp);

        var origenes = json.GetProperty("permisos").EnumerateArray()
            .ToDictionary(p => p.GetProperty("codigo").GetString()!, p => p.GetProperty("origen").GetString());
        Assert.Equal("Rol", origenes["identidad.usuarios.leer"]);
        Assert.Equal("Denegado", origenes["identidad.roles.leer"]);
        Assert.Equal("Concedido", origenes["identidad.usuarios.crear"]);
        Assert.Equal(1, json.GetProperty("concedidos").GetInt32());
        Assert.Equal(1, json.GetProperty("denegados").GetInt32());

        // Otra empresa: sin rol allí no hay permisos ni mezcla de overrides.
        var otra = await admin.GetAsync($"{UsuariosEndpoint}/{u.Id}/empresas/{Guid.NewGuid()}/permisos");
        Assert.Equal(HttpStatusCode.OK, otra.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(otra)).GetProperty("permisos").GetArrayLength());

        // Y no se pueden crear overrides en una empresa donde no tiene rol.
        var put = await admin.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{u.Id}/empresas/{Guid.NewGuid()}/permisos-override",
            new { Overrides = new[] { Item(RolesLeer, "Denegar") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
    }

    [Fact]
    public async Task Sin_gestionar_permisos_el_PUT_da_403()
    {
        var admin = await SuperAdminAsync();
        var rolId = await CrearRolAsync(admin, UsuariosLeer);
        var objetivo = await CrearUsuarioConRolAsync(admin, rolId);
        var sinPermiso = await CrearUsuarioConRolAsync(admin, rolId);
        var client = await ClientDeAsync(sinPermiso.Oid);

        var put = await client.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{objetivo.Id}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = new[] { Item(UsuariosLeer, "Denegar") } });

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task Reglas_de_seguridad_escalada_autoedicion_superadmin_y_validacion()
    {
        var admin = await SuperAdminAsync();
        // Gestor: puede gestionar permisos y leer usuarios, pero NO tiene roles.leer.
        var rolGestor = await CrearRolAsync(admin, GestionarPermisos, UsuariosLeer);
        var gestor = await CrearUsuarioConRolAsync(admin, rolGestor);
        var objetivo = await CrearUsuarioConRolAsync(admin, rolGestor);
        var client = await ClientDeAsync(gestor.Oid);

        // Escalada: conceder lo que no posee.
        var escalada = await client.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{objetivo.Id}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = new[] { Item(RolesLeer, "Conceder") } });
        Assert.Equal(HttpStatusCode.Forbidden, escalada.StatusCode);

        // Auto-edición.
        var auto = await client.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{gestor.Id}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = new[] { Item(UsuariosLeer, "Denegar") } });
        Assert.Equal(HttpStatusCode.Forbidden, auto.StatusCode);

        // Super-admin protegido (gestor tiene alcance en la empresa).
        var superId = await LoginAsync(SuperAdminOid);
        var protegido = await client.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{superId.UsuarioId}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = new[] { Item(UsuariosLeer, "Denegar") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, protegido.StatusCode);

        // Duplicado / ambos efectos / permiso inexistente.
        var dup = await client.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{objetivo.Id}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = new[] { Item(UsuariosLeer, "Denegar"), Item(UsuariosLeer, "Conceder") } });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        var inexistente = await client.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{objetivo.Id}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = new[] { Item(Guid.NewGuid(), "Denegar") } });
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Fact]
    public async Task Rol_de_sistema_no_super_admin_si_admite_excepciones_y_super_admin_sigue_protegido()
    {
        var admin = await SuperAdminAsync();
        var u = await CrearUsuarioConRolAsync(admin, RolAdminCatalogos);

        // admin-catalogos es es_del_sistema=true pero NO super-admin: editable.
        await PutOverridesAsync(admin, u.Id, new[] { Item(UsuariosLeer, "Conceder") });
        var resp = await admin.GetAsync($"{UsuariosEndpoint}/{u.Id}/empresas/{EmpresaId}/permisos");
        resp.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(resp);
        Assert.Equal("admin-catalogos", json.GetProperty("rolCodigo").GetString());
        Assert.False(json.GetProperty("rolEsSuperAdmin").GetBoolean());
        Assert.Equal(1, json.GetProperty("concedidos").GetInt32());
        var del = await admin.DeleteAsync($"{UsuariosEndpoint}/{u.Id}/empresas/{EmpresaId}/permisos-override");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // El super-admin sigue bloqueado (lo edita un gestor distinto, no él mismo) y el GET lo marca.
        var rolGestor = await CrearRolAsync(admin, GestionarPermisos, UsuariosLeer);
        var gestor = await CrearUsuarioConRolAsync(admin, rolGestor);
        var gestorClient = await ClientDeAsync(gestor.Oid);
        var superId = await LoginAsync(SuperAdminOid);
        var protegido = await gestorClient.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{superId.UsuarioId}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = new[] { Item(UsuariosLeer, "Denegar") } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, protegido.StatusCode);
        var getSuper = await ReadJsonAsync(
            await admin.GetAsync($"{UsuariosEndpoint}/{superId.UsuarioId}/empresas/{EmpresaId}/permisos"));
        Assert.True(getSuper.GetProperty("rolEsSuperAdmin").GetBoolean());
    }

    [Fact]
    public async Task Cambiar_o_revocar_el_rol_borra_los_overrides_pero_reasignar_el_mismo_no()
    {
        var admin = await SuperAdminAsync();
        var rolA = await CrearRolAsync(admin, UsuariosLeer, RolesLeer);
        var rolB = await CrearRolAsync(admin, UsuariosLeer);
        var u = await CrearUsuarioConRolAsync(admin, rolA);
        await PutOverridesAsync(admin, u.Id, new[] { Item(RolesLeer, "Denegar") });

        // Mismo rol: 409 y los overrides siguen.
        var dup = await admin.PostAsJsonAsync($"{UsuariosEndpoint}/{u.Id}/asignaciones",
            new { EmpresaId, RolId = rolA });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal(1, await ContarOverridesAsync(admin, u.Id));

        // Rol distinto: se borran.
        var asignarB = await admin.PostAsJsonAsync($"{UsuariosEndpoint}/{u.Id}/asignaciones",
            new { EmpresaId, RolId = rolB });
        Assert.Equal(HttpStatusCode.Created, asignarB.StatusCode);
        Assert.Equal(0, await ContarOverridesAsync(admin, u.Id));

        // Revocar también borra.
        await PutOverridesAsync(admin, u.Id, new[] { Item(UsuariosCrear, "Denegar") });
        Assert.Equal(1, await ContarOverridesAsync(admin, u.Id));
        var detalle = await ReadJsonAsync(await admin.GetAsync($"{UsuariosEndpoint}/{u.Id}"));
        foreach (var a in detalle.GetProperty("asignaciones").EnumerateArray())
        {
            var del = await admin.DeleteAsync($"{UsuariosEndpoint}/asignaciones/{a.GetProperty("id").GetGuid()}");
            Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        }
        Assert.Equal(0, await ContarOverridesAsync(admin, u.Id));
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private sealed record Sesion(string Token, Guid UsuarioId, IReadOnlyList<string> Permisos);
    private sealed record UsuarioPrueba(string Oid, Guid Id);

    private static object Item(Guid permisoId, string efecto, string? motivo = null) =>
        new { PermisoId = permisoId, Efecto = efecto, Motivo = motivo };

    private async Task<HttpClient> SuperAdminAsync() => await ClientDeAsync(SuperAdminOid);

    private async Task<HttpClient> ClientDeAsync(string oid)
    {
        var client = _factory.CreateClientWithIdempotency();
        var sesion = await LoginAsync(oid, client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sesion.Token);
        return client;
    }

    private async Task<Sesion> LoginAsync(string oid, HttpClient? client = null)
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
        return new Sesion(
            json.GetProperty("accessToken").GetString()!,
            json.GetProperty("usuario").GetProperty("id").GetGuid(),
            json.GetProperty("permisos").EnumerateArray().Select(p => p.GetString()!).ToList());
    }

    private static async Task<Guid> CrearRolAsync(HttpClient admin, params Guid[] permisoIds)
    {
        var codigo = $"pp-{Guid.NewGuid():N}"[..16];
        var created = await admin.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol permisos personalizados",
            Descripcion = (string?)null,
        });
        created.EnsureSuccessStatusCode();
        var rolId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var put = await admin.PutAsJsonAsync($"{RolesEndpoint}/{rolId}/permisos", new { PermisoIds = permisoIds });
        put.EnsureSuccessStatusCode();
        return rolId;
    }

    /// <summary>Provisiona un usuario (primer login) y le asigna el rol en la empresa.</summary>
    private async Task<UsuarioPrueba> CrearUsuarioConRolAsync(HttpClient admin, Guid rolId)
    {
        var oid = $"pp-{Guid.NewGuid():N}"[..20];
        var sesion = await LoginAsync(oid);
        var asignar = await admin.PostAsJsonAsync($"{UsuariosEndpoint}/{sesion.UsuarioId}/asignaciones",
            new { EmpresaId, RolId = rolId });
        Assert.Equal(HttpStatusCode.Created, asignar.StatusCode);
        return new UsuarioPrueba(oid, sesion.UsuarioId);
    }

    private static async Task PutOverridesAsync(HttpClient admin, Guid usuarioId, object[] overrides)
    {
        var put = await admin.PutAsJsonAsync(
            $"{UsuariosEndpoint}/{usuarioId}/empresas/{EmpresaId}/permisos-override",
            new { Overrides = overrides });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
    }

    private static async Task<int> ContarOverridesAsync(HttpClient admin, Guid usuarioId)
    {
        var resp = await admin.GetAsync($"{UsuariosEndpoint}/{usuarioId}/empresas/{EmpresaId}/permisos");
        resp.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(resp);
        return json.GetProperty("concedidos").GetInt32() + json.GetProperty("denegados").GetInt32();
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
