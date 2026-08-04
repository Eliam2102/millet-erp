using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Tests integration del CRUD de Roles + matriz de permisos + grupos
/// Entra ID (F-Admin-PR3.2). Cubre los 8 endpoints con happy paths y
/// caminos negativos clave (401 sin token, 409 código duplicado, 422
/// rol del sistema, 200/204 flujos completos).
///
/// <para>
/// Cada test genera sufijos aleatorios para sus claves (codigo, objectId)
/// para no chocar con otros tests corriendo en la misma DB compartida ni
/// con los roles del bootstrap (super-admin).
/// </para>
/// </summary>
public class RolesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string RolesEndpoint = "/api/v1/identidad/roles";

    private readonly WebApplicationFactory<Program> _factory;

    public RolesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Listar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(RolesEndpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Con_SuperAdmin_Retorna_200_Con_Super_Admin_Rol()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(RolesEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("total").GetInt32() >= 1,
            "Debe haber al menos el rol 'super-admin' del bootstrap.");
    }

    [Fact]
    public async Task Crear_Con_Codigo_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomCodigo();

        var primero = await client.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol Test Primero",
            Descripcion = (string?)null,
        });
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol Test Duplicado",
            Descripcion = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    [Fact]
    public async Task Crear_Con_Datos_Validos_Retorna_201_Con_Id()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomCodigo();

        var response = await client.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol Test Crear",
            Descripcion = "Descripción de test",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(codigo, body.GetProperty("codigo").GetString());
        Assert.True(body.GetProperty("activo").GetBoolean());
        Assert.False(body.GetProperty("esDelSistema").GetBoolean());
    }

    [Fact]
    public async Task Patch_Cambia_Nombre_Y_Get_Posterior_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomCodigo();

        var created = await client.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Nombre Original",
            Descripcion = (string?)null,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync($"{RolesEndpoint}/{id}", new
        {
            Nombre = "Nombre Actualizado",
            Descripcion = (string?)null,
            LimpiarDescripcion = false,
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        var verify = await client.GetAsync($"{RolesEndpoint}/{id}");
        verify.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(verify);
        var rol = body.GetProperty("rol");
        Assert.Equal("Nombre Actualizado", rol.GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task Delete_Rol_Del_Sistema_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();

        // Localizar el rol super-admin (esDelSistema=true).
        var list = await client.GetAsync(RolesEndpoint + "?limit=200");
        list.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(list);
        var items = body.GetProperty("items");
        Guid? superAdminId = null;
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("esDelSistema").GetBoolean())
            {
                superAdminId = item.GetProperty("id").GetGuid();
                break;
            }
        }
        Assert.NotNull(superAdminId);

        var response = await client.DeleteAsync($"{RolesEndpoint}/{superAdminId}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Put_Permisos_Asigna_Y_Get_Detalle_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomCodigo();

        // Crear rol vacío.
        var createdResp = await client.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol Asignacion Permisos",
            Descripcion = (string?)null,
        });
        createdResp.EnsureSuccessStatusCode();
        var rolId = (await ReadJsonAsync(createdResp)).GetProperty("id").GetGuid();

        // Obtener un par de permisos del catálogo (seed determinista).
        var permisoIds = new[]
        {
            // identidad.usuarios.leer + identidad.roles.leer.
            Guid.Parse("00000002-0002-0000-0000-000000000001"),
            Guid.Parse("00000002-0002-0000-0000-000000000004"),
        };

        var put = await client.PutAsJsonAsync($"{RolesEndpoint}/{rolId}/permisos", new
        {
            PermisoIds = permisoIds,
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var detalle = await client.GetAsync($"{RolesEndpoint}/{rolId}");
        detalle.EnsureSuccessStatusCode();
        var detalleBody = await ReadJsonAsync(detalle);
        var asignados = detalleBody.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(e => e.GetGuid())
            .ToHashSet();
        Assert.Equal(permisoIds.ToHashSet(), asignados);
    }

    [Fact]
    public async Task Post_Grupo_Entra_Id_Retorna_201_Y_Duplicado_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomCodigo();

        // Crear rol.
        var createdResp = await client.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol Grupo Entra ID",
            Descripcion = (string?)null,
        });
        createdResp.EnsureSuccessStatusCode();
        var rolId = (await ReadJsonAsync(createdResp)).GetProperty("id").GetGuid();

        var objectId = Guid.NewGuid().ToString();
        var primera = await client.PostAsJsonAsync(
            $"{RolesEndpoint}/{rolId}/grupos-entra-id", new
        {
            ObjectId = objectId,
            Nombre = "Compras-Jefes",
        });
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var duplicada = await client.PostAsJsonAsync(
            $"{RolesEndpoint}/{rolId}/grupos-entra-id", new
        {
            ObjectId = objectId,
            Nombre = "Compras-Jefes",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);
    }

    [Fact]
    public async Task Delete_Grupo_Entra_Id_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomCodigo();

        var createdResp = await client.PostAsJsonAsync(RolesEndpoint, new
        {
            Id = Guid.Empty,
            Codigo = codigo,
            Nombre = "Rol Grupo Para Borrar",
            Descripcion = (string?)null,
        });
        createdResp.EnsureSuccessStatusCode();
        var rolId = (await ReadJsonAsync(createdResp)).GetProperty("id").GetGuid();

        var asociar = await client.PostAsJsonAsync(
            $"{RolesEndpoint}/{rolId}/grupos-entra-id", new
        {
            ObjectId = Guid.NewGuid().ToString(),
            Nombre = "Grupo Test",
        });
        asociar.EnsureSuccessStatusCode();
        var asocId = (await ReadJsonAsync(asociar)).GetProperty("id").GetGuid();

        var del = await client.DeleteAsync(
            $"{RolesEndpoint}/grupos-entra-id/{asocId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    // --- Helpers ---

    /// <summary>
    /// Código de rol único para evitar choques entre tests. Kebab simple
    /// comenzando con letra (mismo formato que los roles del bootstrap:
    /// <c>super-admin</c>, <c>admin-compras</c>, etc.). Sufijo aleatorio
    /// garantiza unicidad entre tests concurrentes.
    /// </summary>
    private static string RandomCodigo()
    {
        var sufijo = Guid.NewGuid().ToString("N").Substring(0, 6);
        return $"test-roles-dyn-{sufijo}";
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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
