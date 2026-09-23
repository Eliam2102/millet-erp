using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Tests integration de los endpoints de Usuarios. Cubre:
/// <list type="bullet">
///   <item>Catálogo legacy <c>GET /</c> (B.1) — selectores de UI.</item>
///   <item>CRUD admin (F-Admin-PR4.2): list, crear, detalle, patch,
///         desactivar, reactivar.</item>
///   <item>Asignaciones (F-Admin-PR4.2): POST y DELETE con invariante
///         "último super-admin".</item>
/// </list>
///
/// <para>
/// Cada test genera datos con sufijo aleatorio para no chocar con otros
/// tests corriendo en la misma BD compartida ni con el bootstrap del
/// superadmin.
/// </para>
/// </summary>
public class UsuariosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private const string UsuariosEndpoint = "/api/v1/identidad/usuarios";

    private readonly WebApplicationFactory<Program> _factory;

    public UsuariosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ====================================================================
    // CATÁLOGO LEGACY (B.1) — preservados de PR3.x.
    // ====================================================================

    [Fact]
    public async Task ListarUsuarios_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(UsuariosEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarUsuarios_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(UsuariosEndpoint);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListarUsuarios_Con_Permiso_Retorna_Al_Menos_SuperAdmin()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(UsuariosEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);

        // El catálogo legacy no debe exponer entraOid.
        foreach (var item in items.EnumerateArray())
        {
            Assert.False(item.TryGetProperty("entraOid", out _),
                "entraOid no debe estar en el shape público del catálogo legacy");
        }
    }

    [Fact]
    public async Task ListarUsuarios_Filtra_Por_Activo_True()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(UsuariosEndpoint + "?activo=true");

        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        foreach (var item in items.EnumerateArray())
        {
            Assert.True(item.GetProperty("activo").GetBoolean());
        }
    }

    [Fact]
    public async Task ListarUsuarios_Q_Substring_En_Nombre_O_Email()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(UsuariosEndpoint + "?q=superadmin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Me_Trae_DepartamentoId_Cuando_Bootstrap_Lo_Asigno()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.TryGetProperty("departamentoId", out var deptoProp),
            "MeResponse debe exponer departamentoId");
        Assert.NotEqual(JsonValueKind.Null, deptoProp.ValueKind);
        Assert.Equal(
            Guid.Parse("00000005-0004-0000-0000-000000000001"),
            deptoProp.GetGuid());
    }

    // ====================================================================
    // ADMIN CRUD (F-Admin-PR4.2).
    // ====================================================================

    [Fact]
    public async Task Admin_List_Con_SuperAdmin_Incluye_SuperAdmin_Y_EntraOid()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(UsuariosEndpoint + "/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("total").GetInt32() >= 1);

        // El shape admin SÍ expone entraOid.
        var hayEntraOid = false;
        foreach (var item in json.GetProperty("items").EnumerateArray())
        {
            if (item.TryGetProperty("entraOid", out _)) { hayEntraOid = true; break; }
        }
        Assert.True(hayEntraOid, "Shape admin debe incluir entraOid");
    }

    [Fact]
    public async Task Crear_Sin_EntraIdObjectId_Genera_Placeholder_Dev_Email()
    {
        var client = await CreateSuperAdminClientAsync();
        var email = $"user-{RandomSufijo()}@test.local";

        var response = await client.PostAsJsonAsync(UsuariosEndpoint, new
        {
            Id = Guid.Empty,
            Email = email,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Usuario Sin Oid",
            DepartamentoId = (Guid?)null,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal($"pending:{email}", body.GetProperty("entraOid").GetString());
        Assert.True(body.GetProperty("activo").GetBoolean());
    }

    [Fact]
    public async Task Crear_Con_Email_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var email = $"dup-{RandomSufijo()}@test.local";

        var primero = await client.PostAsJsonAsync(UsuariosEndpoint, new
        {
            Id = Guid.Empty,
            Email = email,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Primero",
            DepartamentoId = (Guid?)null,
        });
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsJsonAsync(UsuariosEndpoint, new
        {
            Id = Guid.Empty,
            Email = email,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Duplicado",
            DepartamentoId = (Guid?)null,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Patch_Cambia_Nombre_Y_Get_Posterior_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();
        var email = $"patch-{RandomSufijo()}@test.local";

        var created = await client.PostAsJsonAsync(UsuariosEndpoint, new
        {
            Id = Guid.Empty,
            Email = email,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Nombre Original",
            DepartamentoId = (Guid?)null,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync($"{UsuariosEndpoint}/{id}", new
        {
            Email = (string?)null,
            NombreCompleto = "Nombre Actualizado",
            DepartamentoId = (Guid?)null,
            LimpiarDepartamento = false,
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        var verify = await client.GetAsync($"{UsuariosEndpoint}/{id}");
        verify.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(verify);
        var usuario = body.GetProperty("usuario");
        Assert.Equal("Nombre Actualizado", usuario.GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Desactivar_Usuario_Regular_Retorna_200()
    {
        var client = await CreateSuperAdminClientAsync();
        var email = $"deact-{RandomSufijo()}@test.local";

        var created = await client.PostAsJsonAsync(UsuariosEndpoint, new
        {
            Id = Guid.Empty,
            Email = email,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Para Desactivar",
            DepartamentoId = (Guid?)null,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var resp = await client.PostAsync(
            $"{UsuariosEndpoint}/{id}/desactivar", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await ReadJsonAsync(resp);
        Assert.False(body.GetProperty("activo").GetBoolean());
    }

    [Fact]
    public async Task Desactivar_Ultimo_Super_Admin_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();

        // El bootstrap garantiza un super-admin con asignación. Localizarlo
        // por su EntraOid conocido ("dev-superadmin").
        var superAdminId = await LocalizarUsuarioSuperAdminAsync(client);

        var resp = await client.PostAsync(
            $"{UsuariosEndpoint}/{superAdminId}/desactivar", content: null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);

        var problem = await ReadJsonAsync(resp);
        // El GlobalExceptionHandler expone el code en ProblemDetails.
        // No depender del shape exacto: validar presencia del 422 alcanza
        // si el helper de exception → ProblemDetails está estable.
        Assert.True(problem.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task Reactivar_Usuario_Desactivado_Retorna_200_Activo_True()
    {
        var client = await CreateSuperAdminClientAsync();
        var email = $"react-{RandomSufijo()}@test.local";

        var created = await client.PostAsJsonAsync(UsuariosEndpoint, new
        {
            Id = Guid.Empty,
            Email = email,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Para Reactivar",
            DepartamentoId = (Guid?)null,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        (await client.PostAsync($"{UsuariosEndpoint}/{id}/desactivar", null))
            .EnsureSuccessStatusCode();

        var resp = await client.PostAsync($"{UsuariosEndpoint}/{id}/reactivar", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        Assert.True(body.GetProperty("activo").GetBoolean());
    }

    // ====================================================================
    // ASIGNACIONES (F-Admin-PR4.2).
    // ====================================================================

    [Fact]
    public async Task Asignar_Rol_A_Usuario_Retorna_201_Y_Duplicado_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var (empresaId, rolId) = await LocalizarEmpresaYRolNoSuperAdminAsync(client);
        var usuarioId = await CrearUsuarioAuxiliarAsync(client);

        var primera = await client.PostAsJsonAsync(
            $"{UsuariosEndpoint}/{usuarioId}/asignaciones",
            new { EmpresaId = empresaId, RolId = rolId });

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var duplicada = await client.PostAsJsonAsync(
            $"{UsuariosEndpoint}/{usuarioId}/asignaciones",
            new { EmpresaId = empresaId, RolId = rolId });

        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);
    }

    [Fact]
    public async Task Revocar_Asignacion_Regular_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var (empresaId, rolId) = await LocalizarEmpresaYRolNoSuperAdminAsync(client);
        var usuarioId = await CrearUsuarioAuxiliarAsync(client);

        var asignar = await client.PostAsJsonAsync(
            $"{UsuariosEndpoint}/{usuarioId}/asignaciones",
            new { EmpresaId = empresaId, RolId = rolId });
        asignar.EnsureSuccessStatusCode();
        var asignacionId = (await ReadJsonAsync(asignar)).GetProperty("id").GetGuid();

        var del = await client.DeleteAsync(
            $"{UsuariosEndpoint}/asignaciones/{asignacionId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Revocar_Unica_Asignacion_De_Super_Admin_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();

        // Localizar la asignación super-admin del bootstrap.
        var superAdminUserId = await LocalizarUsuarioSuperAdminAsync(client);

        var detalle = await client.GetAsync($"{UsuariosEndpoint}/{superAdminUserId}");
        detalle.EnsureSuccessStatusCode();
        var detalleBody = await ReadJsonAsync(detalle);

        // Encontrar la asignación cuyo rolCodigo es "super-admin".
        Guid? asignacionSuperAdmin = null;
        foreach (var a in detalleBody.GetProperty("asignaciones").EnumerateArray())
        {
            if (a.GetProperty("rolCodigo").GetString() == "super-admin")
            {
                asignacionSuperAdmin = a.GetProperty("id").GetGuid();
                break;
            }
        }
        Assert.NotNull(asignacionSuperAdmin);

        var del = await client.DeleteAsync(
            $"{UsuariosEndpoint}/asignaciones/{asignacionSuperAdmin}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, del.StatusCode);
    }

    // ====================================================================
    // Helpers.
    // ====================================================================

    private static string RandomSufijo() =>
        Guid.NewGuid().ToString("N").Substring(0, 8);

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearUsuarioAuxiliarAsync(HttpClient client)
    {
        var email = $"aux-{RandomSufijo()}@test.local";
        var resp = await client.PostAsJsonAsync(UsuariosEndpoint, new
        {
            Id = Guid.Empty,
            Email = email,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Usuario Auxiliar",
            DepartamentoId = (Guid?)null,
        });
        resp.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(resp)).GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Devuelve (empresaId, rolId) donde el rol NO es super-admin — para
    /// que el test de asignar/revocar no toque la invariante de "último
    /// super-admin". Usa la primera empresa del bootstrap y un rol del
    /// catálogo MVP distinto al super-admin.
    /// </summary>
    private static async Task<Guid> LocalizarUsuarioSuperAdminAsync(HttpClient client)
    {
        // Filtrar por el rol super-admin: la BD de dev compartida acumula
        // usuarios de corridas previas y el listado topa en 200 por página,
        // así que buscar el EntraOid en la primera página sin filtro deja de
        // encontrarlo en cuanto hay más de 200 usuarios.
        var rolesResp = await client.GetAsync("/api/v1/identidad/roles?limit=200");
        rolesResp.EnsureSuccessStatusCode();
        var rolesBody = await ReadJsonAsync(rolesResp);
        Guid? superAdminRolId = null;
        foreach (var r in rolesBody.GetProperty("items").EnumerateArray())
        {
            if (r.GetProperty("codigo").GetString() == "super-admin")
            {
                superAdminRolId = r.GetProperty("id").GetGuid();
                break;
            }
        }
        Assert.NotNull(superAdminRolId);

        var adminList = await client.GetAsync(
            $"{UsuariosEndpoint}/admin?rolId={superAdminRolId}&limit=200");
        adminList.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(adminList);
        foreach (var item in body.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("entraOid").GetString() == SuperAdminOid)
            {
                return item.GetProperty("id").GetGuid();
            }
        }

        Assert.Fail($"No se encontró el usuario super-admin ({SuperAdminOid}).");
        return Guid.Empty;
    }

    private static async Task<(Guid EmpresaId, Guid RolId)> LocalizarEmpresaYRolNoSuperAdminAsync(
        HttpClient client)
    {
        // EmpresaId del bootstrap (idempotente, determinista).
        var empresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");

        // Rol no super-admin: tomamos cualquier rol del catálogo MVP cuyo
        // codigo no sea "super-admin". El bootstrap garantiza ≥6 roles MVP.
        var rolesResp = await client.GetAsync("/api/v1/identidad/roles?limit=200");
        rolesResp.EnsureSuccessStatusCode();
        var rolesBody = await ReadJsonAsync(rolesResp);
        Guid? rolId = null;
        foreach (var r in rolesBody.GetProperty("items").EnumerateArray())
        {
            if (r.GetProperty("codigo").GetString() != "super-admin")
            {
                rolId = r.GetProperty("id").GetGuid();
                break;
            }
        }
        Assert.NotNull(rolId);
        return (empresaId, rolId.Value);
    }

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
