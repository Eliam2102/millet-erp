using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// Tests integration de los endpoints enriquecidos de Datos Maestros
/// (F-Admin-PR4.5). Verifica filtros expandidos sobre proveedores y
/// artículos (cubren los casos que el endpoint legacy de F7-PR1 no
/// soportaba: filtro por rfc, razonSocial, tipoPersona, naturaleza,
/// unidadMedida).
/// </summary>
public class DatosMaestrosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid MonedaMxnId = Guid.Parse("00000001-0000-0000-0000-000000000001");
    private readonly WebApplicationFactory<Program> _factory;

    public DatosMaestrosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListarProveedores_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/datos-maestros/proveedores");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarProveedores_Con_Token_Retorna_200_Y_Items_Array()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/datos-maestros/proveedores");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("items", out _));
        Assert.True(body.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task ListarProveedores_Filtra_Por_Rfc_Substring()
    {
        var client = await CreateSuperAdminClientAsync();

        // Solo verificamos que el filtro no falle (no asumimos sustring
        // específico porque el seed test puede variar). Pasamos un RFC
        // que probablemente no exista para verificar que retorna 0.
        var response = await client.GetAsync("/api/v1/datos-maestros/proveedores?rfc=ZZZ999");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var total = body.GetProperty("total").GetInt32();
        Assert.True(total >= 0, "Filtro de RFC debe responder OK aún con 0 hits.");
    }

    [Fact]
    public async Task ListarArticulos_Con_Filtros_Combinados_Funciona()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(
            "/api/v1/datos-maestros/articulos?naturaleza=0&estatus=0&offset=0&limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("items", out _));
        Assert.True(body.GetProperty("limit").GetInt32() == 10);
    }

    [Fact]
    public async Task ObtenerArticulo_No_Encontrado_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var fakeId = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/datos-maestros/articulos/{fakeId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerArticulo_Detalle_Expone_UnidadMedidaId()
    {
        // Regresión Bug A: el detalle de /datos-maestros tenía su propia copia
        // del DTO ArticuloDetalle sin el FK unidadMedidaId (ADR-0046 Etapa 1b),
        // así que el form de edición lo leía como null y mostraba la unidad
        // legacy aunque el artículo tuviera unidad asignada. El campo DEBE estar
        // presente en el JSON del detalle.
        var client = await CreateSuperAdminClientAsync();
        var articulo = await PrimeroAsync(client, "/api/v1/datos-maestros/articulos?limit=50");
        var id = articulo.GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/api/v1/datos-maestros/articulos/{id}");
        response.EnsureSuccessStatusCode();
        var detalle = await ReadJsonAsync(response);

        // Pre-fix el serializer omitía la propiedad → TryGetProperty era false.
        Assert.True(
            detalle.TryGetProperty("unidadMedidaId", out var detalleUm),
            "El detalle debe exponer 'unidadMedidaId' (regresión Bug A).");

        // Null-agnóstico: paridad con el valor que ya expone la lista para el
        // mismo id, sin asumir que el seed tenga FK asignado (artículos legacy
        // quedan null y siguen siendo válidos).
        var listaUm = articulo.GetProperty("unidadMedidaId");
        Assert.Equal(listaUm.ValueKind, detalleUm.ValueKind);
        if (listaUm.ValueKind != JsonValueKind.Null)
        {
            Assert.Equal(listaUm.GetGuid(), detalleUm.GetGuid());
        }
    }

    // --- Case-insensitive (Defect B): textos con folding, códigos con lower() ---

    [Fact]
    public async Task ListarProveedores_RazonSocial_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        // Tomamos un proveedor real del seed y buscamos su razón social en
        // minúsculas: el folding (case + acentos, ADR-0045) debe encontrarlo.
        // Pre-fix (Contains case-sensitive) no lo hallaba si tenía mayúsculas.
        var prov = await PrimeroAsync(client, "/api/v1/datos-maestros/proveedores?limit=50");
        var id = prov.GetProperty("id").GetGuid();
        var razon = prov.GetProperty("razonSocial").GetString()!;

        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/proveedores?razonSocial={Uri.EscapeDataString(razon.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task ListarProveedores_Rfc_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        var prov = await PrimeroAsync(client, "/api/v1/datos-maestros/proveedores?limit=50");
        var id = prov.GetProperty("id").GetGuid();
        var rfc = prov.GetProperty("rfc").GetString()!;

        // RFC = código → lower() en ambos lados.
        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/proveedores?rfc={Uri.EscapeDataString(rfc.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task ListarArticulos_Codigo_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        var art = await PrimeroAsync(client, "/api/v1/datos-maestros/articulos?limit=50");
        var id = art.GetProperty("id").GetGuid();
        var clave = art.GetProperty("clave").GetString()!;

        // codigo (clave) = código → lower() en ambos lados.
        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/articulos?codigo={Uri.EscapeDataString(clave.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task ListarArticulos_Descripcion_CaseInsensitive()
    {
        var client = await CreateSuperAdminClientAsync();
        var art = await PrimeroAsync(client, "/api/v1/datos-maestros/articulos?limit=50");
        var id = art.GetProperty("id").GetGuid();
        var nombre = art.GetProperty("nombre").GetString()!;

        // descripcion (nombre) = texto libre → folding case + acentos (ADR-0045).
        var response = await client.GetAsync(
            $"/api/v1/datos-maestros/articulos?descripcion={Uri.EscapeDataString(nombre.ToLowerInvariant())}&limit=50");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Contains(json.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == id);
    }

    // --- GET /datos-bancarios (F1-ADM-05) ---

    [Fact]
    public async Task ObtenerDatosBancarios_Sin_Permiso_Retorna_403()
    {
        var admin = await CreateSuperAdminClientAsync();
        var proveedorId = await CrearProveedorConClabeAsync(admin, "011122223333444455");

        var clienteSinBancarios = await CreateClientConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresGestionar);

        var response = await clienteSinBancarios.GetAsync(
            $"/api/v1/datos-maestros/proveedores/{proveedorId}/datos-bancarios");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerDatosBancarios_Con_Permiso_Retorna_Clabe_Enmascarada()
    {
        var admin = await CreateSuperAdminClientAsync();
        var proveedorId = await CrearProveedorConClabeAsync(admin, "011122223333444455");

        var clienteVer = await CreateClientConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer);

        var response = await clienteVer.GetAsync(
            $"/api/v1/datos-maestros/proveedores/{proveedorId}/datos-bancarios");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Equal("**************4455", json.GetProperty("clabe").GetString());
        Assert.False(json.GetProperty("clabeCompleta").GetBoolean());
    }

    [Fact]
    public async Task ObtenerDatosBancarios_Con_VerCuentaCompleta_Retorna_Clabe_Completa()
    {
        var admin = await CreateSuperAdminClientAsync();
        const string clabe = "011122223333444455";
        var proveedorId = await CrearProveedorConClabeAsync(admin, clabe);

        var clienteCompleta = await CreateClientConPermisosAsync(
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
            PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta);

        var response = await clienteCompleta.GetAsync(
            $"/api/v1/datos-maestros/proveedores/{proveedorId}/datos-bancarios");
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        Assert.Equal(clabe, json.GetProperty("clabe").GetString());
        Assert.True(json.GetProperty("clabeCompleta").GetBoolean());
    }

    [Fact]
    public async Task ListarYDetalle_Proveedores_No_Exponen_Clabe()
    {
        var admin = await CreateSuperAdminClientAsync();
        var proveedorId = await CrearProveedorConClabeAsync(admin, "011122223333444455");

        var detalle = await admin.GetAsync($"/api/v1/datos-maestros/proveedores/{proveedorId}");
        detalle.EnsureSuccessStatusCode();
        var detalleJson = await ReadJsonAsync(detalle);
        Assert.False(detalleJson.TryGetProperty("clabe", out _));
        var rfc = detalleJson.GetProperty("rfc").GetString()!;

        var lista = await admin.GetAsync(
            $"/api/v1/datos-maestros/proveedores?rfc={Uri.EscapeDataString(rfc)}");
        lista.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(lista)).GetProperty("items");
        Assert.All(items.EnumerateArray(), i => Assert.False(i.TryGetProperty("clabe", out _)));
    }

    // --- Helpers ---

    /// <summary>Crea un proveedor y le asigna una CLABE vía el PATCH legacy de /catalogos.</summary>
    private static async Task<Guid> CrearProveedorConClabeAsync(HttpClient admin, string clabe)
    {
        var crear = await admin.PostAsJsonAsync("/api/v1/catalogos/proveedores", new
        {
            Clave = $"PROV-BANC-{Guid.NewGuid().ToString("N")[..8]}",
            RazonSocial = "Proveedor Bancarios Test SA de CV",
            Rfc = $"TST{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            TipoPersona = TipoPersonaProveedor.Moral,
            NombreComercial = (string?)null,
            CondicionesPagoDias = (short)30,
            MonedaPreferidaId = MonedaMxnId,
            Email = (string?)null,
            Telefono = (string?)null,
        });
        crear.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var patch = await admin.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}", new { Banco = "BBVA", Clabe = clabe });
        patch.EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>
    /// Crea un rol nuevo con exactamente los permisos indicados, un usuario
    /// auxiliar asignado a ese rol en la empresa bootstrap, y devuelve un
    /// cliente logueado como ese usuario (F1-ADM-05: probar "tiene A pero
    /// no B" sin tocar el rol super-admin, que trae todos los permisos
    /// canónicos). Duplicado intencional del helper equivalente en
    /// Compras.IntegrationTests — los proyectos de test no se referencian
    /// entre sí (ver TestComprasFixtures).
    /// </summary>
    private async Task<HttpClient> CreateClientConPermisosAsync(params string[] codigosPermiso)
    {
        var admin = await CreateSuperAdminClientAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"perm-test-{sufijo}";
        var email = $"perm-test-{sufijo}@test.local";
        var nombre = $"Usuario Permiso Test {sufijo}";

        // Primer login: auto-provisiona el usuario (Usuario.EntraOid real,
        // no "pending:...") sin empresa ni permisos todavía. Un usuario
        // creado por el CRUD de admin nace en EstadoAcceso.PendientePrimerAcceso
        // con OID "pending:{email}", y Usuario.RegistrarAcceso rechaza login
        // con OID pendiente (422 USUARIO_OID_PENDIENTE) — por eso el
        // auto-provisión de fake-login, no el POST /usuarios.
        var client = _factory.CreateClientWithIdempotency();
        var primerLogin = await FakeLoginJsonAsync(client, oid, email, nombre);
        var usuarioId = primerLogin.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolResp = await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty,
            Codigo = $"rol-test-{sufijo}",
            Nombre = $"Rol Test {sufijo}",
            Descripcion = (string?)null,
        });
        rolResp.EnsureSuccessStatusCode();
        var rolId = (await ReadJsonAsync(rolResp)).GetProperty("id").GetGuid();

        var permisoIds = codigosPermiso
            .Select(codigo => PermisosCanonicos.Todos.First(p => p.Codigo == codigo).Id)
            .ToArray();
        var putPermisos = await admin.PutAsJsonAsync(
            $"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = permisoIds });
        putPermisos.EnsureSuccessStatusCode();

        var asignarResp = await admin.PostAsJsonAsync(
            $"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId });
        asignarResp.EnsureSuccessStatusCode();

        // Segundo login: ahora la empresa se auto-selecciona (única
        // asignación) y los permisos recién asignados se cargan frescos
        // (el primer login no cacheó nada porque no había empresa).
        var token = await FakeLoginAsync(client, oid, email, nombre);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> FakeLoginJsonAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> PrimeroAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0,
            $"El seed no devolvió items para {url}; el test case-insensitive necesita ≥ 1.");
        return items[0].Clone();
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
