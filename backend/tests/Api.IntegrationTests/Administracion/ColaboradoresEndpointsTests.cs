using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Alta unificada de colaborador, caminos A y C (F1-ADM-01, plan 15, F3):
/// (el camino B vive en <see cref="ColaboradoresCuentaNuevaTests"/>)
/// <c>POST /api/v1/admin/colaboradores</c> contra el directorio Entra
/// simulado. Cada test crea su propia cuenta en el simulador y su propia
/// sucursal/departamento/puesto para no chocar con otros tests.
/// </summary>
public class ColaboradoresEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    internal const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms-colab";
    internal const string ColaboradoresEndpoint = "/api/v1/admin/colaboradores";
    private const string EmpresasBase = "/api/v1/admin/empresas";

    private const int SinAcceso = 0;
    private const int CuentaExistente = 1;

    private readonly WebApplicationFactory<Program> _factory;

    public ColaboradoresEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClientWithIdempotency();
        var response = await client.PostAsJsonAsync(ColaboradoresEndpoint, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms-colab@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(ColaboradoresEndpoint, new
        {
            Id = Guid.Empty, Clave = "X", Nombre = "X",
            SucursalId = Guid.NewGuid(), DepartamentoId = Guid.NewGuid(), PuestoId = Guid.NewGuid(),
            Acceso = SinAcceso,
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Sin_Acceso_Crea_Solo_Empleado()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);

        var response = await PostAltaAsync(client, org, SinAcceso, correo: null, rolId: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var empleado = body.GetProperty("empleado");
        Assert.Equal(org.SucursalId, empleado.GetProperty("sucursalId").GetGuid());
        Assert.Equal(org.PuestoId, empleado.GetProperty("puestoId").GetGuid());
        Assert.Equal(JsonValueKind.Null, empleado.GetProperty("usuarioId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("acceso").ValueKind);
    }

    [Fact]
    public async Task Cuenta_Existente_Crea_Usuario_Empleado_Rol_Y_Sucursal_En_Una_Operacion()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var cuenta = await CrearCuentaEntraSimuladaAsync();

        var response = await PostAltaAsync(client, org, CuentaExistente, cuenta.Upn, org.RolId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var acceso = body.GetProperty("acceso");
        var usuarioId = acceso.GetProperty("usuarioId").GetGuid();
        var empleadoId = body.GetProperty("empleado").GetProperty("id").GetGuid();
        Assert.Equal(usuarioId, body.GetProperty("empleado").GetProperty("usuarioId").GetGuid());
        Assert.Equal(cuenta.ObjectId, acceso.GetProperty("entraOid").GetString());
        Assert.Equal((int)EstadoAcceso.PendientePrimerAcceso, acceso.GetProperty("estadoAcceso").GetInt32());
        Assert.False(acceso.GetProperty("usuarioReutilizado").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var usuario = await identidad.Usuarios.SingleAsync(u => u.Id == usuarioId);
        Assert.Equal(org.DepartamentoId, usuario.DepartamentoId);
        Assert.True(await identidad.UsuarioEmpresaRoles.AnyAsync(
            r => r.UsuarioId == usuarioId && r.RolId == org.RolId));
        Assert.True(await identidad.UsuarioSucursales.AnyAsync(
            s => s.UsuarioId == usuarioId && s.SucursalId == org.SucursalId));

        // Usuario y Empleado quedan bajo la misma correlación de auditoría.
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var correlaciones = await compartido.Set<AuditLogEntry>()
            .Where(a => a.EntidadId == usuarioId || a.EntidadId == empleadoId)
            .Select(a => a.CorrelationId)
            .Distinct()
            .ToListAsync();
        Assert.Single(correlaciones);
    }

    [Fact]
    public async Task Sin_RolId_Usa_El_Rol_Sugerido_Del_Puesto()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client, rolSugerido: true);
        var cuenta = await CrearCuentaEntraSimuladaAsync();

        var response = await PostAltaAsync(client, org, CuentaExistente, cuenta.Upn, rolId: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var acceso = (await ReadJsonAsync(response)).GetProperty("acceso");
        Assert.Equal(org.RolId, acceso.GetProperty("rolId").GetGuid());
    }

    [Fact]
    public async Task Sin_RolId_Ni_Rol_Sugerido_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var cuenta = await CrearCuentaEntraSimuladaAsync();

        var response = await PostAltaAsync(client, org, CuentaExistente, cuenta.Upn, rolId: null);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "ALTA_ROL_REQUERIDO");
    }

    [Fact]
    public async Task Reutiliza_Usuario_Existente_Sin_Empleado_Y_Le_Vincula_El_Oid_Real()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var cuenta = await CrearCuentaEntraSimuladaAsync();
        var crearUsuario = await client.PostAsJsonAsync("/api/v1/identidad/usuarios", new
        {
            Id = Guid.Empty,
            Email = cuenta.Upn,
            EntraIdObjectId = (string?)null,
            NombreCompleto = "Usuario Previo",
            DepartamentoId = (Guid?)null,
        });
        Assert.Equal(HttpStatusCode.Created, crearUsuario.StatusCode);
        var usuarioPrevioId = (await ReadJsonAsync(crearUsuario)).GetProperty("id").GetGuid();

        var response = await PostAltaAsync(client, org, CuentaExistente, cuenta.Upn, org.RolId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var acceso = (await ReadJsonAsync(response)).GetProperty("acceso");
        Assert.True(acceso.GetProperty("usuarioReutilizado").GetBoolean());
        Assert.Equal(usuarioPrevioId, acceso.GetProperty("usuarioId").GetGuid());
        Assert.Equal(cuenta.ObjectId, acceso.GetProperty("entraOid").GetString());
    }

    [Fact]
    public async Task Usuario_Ya_Vinculado_A_Otro_Empleado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var cuenta = await CrearCuentaEntraSimuladaAsync();
        var primera = await PostAltaAsync(client, org, CuentaExistente, cuenta.Upn, org.RolId);
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var segunda = await PostAltaAsync(client, org, CuentaExistente, cuenta.Upn, org.RolId);

        await AssertProblemAsync(segunda, HttpStatusCode.Conflict, "USUARIO_YA_VINCULADO");
    }

    [Fact]
    public async Task Falla_Del_Empleado_No_Deja_Usuario_Creado()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);
        var clave = RandomClave("CLB");
        var previo = await PostAltaAsync(client, org, SinAcceso, correo: null, rolId: null, clave);
        Assert.Equal(HttpStatusCode.Created, previo.StatusCode);
        var cuenta = await CrearCuentaEntraSimuladaAsync();

        var response = await PostAltaAsync(client, org, CuentaExistente, cuenta.Upn, org.RolId, clave);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "EMPLEADO_CLAVE_DUPLICADA");
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        Assert.False(await identidad.Usuarios.AnyAsync(u => u.Email == cuenta.Upn));
    }

    [Fact]
    public async Task Cuenta_Inexistente_En_Entra_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);

        var response = await PostAltaAsync(
            client, org, CuentaExistente, $"nadie-{RandomSufijo()}@millet.mx", org.RolId);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "ENTRA_CUENTA_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Dominio_No_Permitido_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);

        var response = await PostAltaAsync(
            client, org, CuentaExistente, $"externo-{RandomSufijo()}@gmail.com", org.RolId);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "ENTRA_DOMINIO_NO_PERMITIDO");
    }

    [Fact]
    public async Task Con_Acceso_Sin_Correo_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        var org = await CrearOrganizacionAsync(client);

        var response = await PostAltaAsync(client, org, CuentaExistente, correo: null, org.RolId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    internal sealed record Organizacion(Guid SucursalId, Guid DepartamentoId, Guid PuestoId, Guid RolId);

    internal static Task<HttpResponseMessage> PostAltaAsync(
        HttpClient client, Organizacion org, int acceso, string? correo, Guid? rolId, string? clave = null)
        => client.PostAsJsonAsync(ColaboradoresEndpoint, new
        {
            Id = Guid.Empty,
            Clave = clave ?? RandomClave("CLB"),
            Nombre = "Colaborador de Prueba",
            org.SucursalId,
            org.DepartamentoId,
            org.PuestoId,
            Acceso = acceso,
            CorreoCorporativo = correo,
            EmailContacto = "contacto.personal@gmail.com",
            RolId = rolId,
        });

    /// <summary>
    /// Sucursal con un departamento asignado y un puesto asignado a ese
    /// departamento. Con <paramref name="rolSugerido"/> el puesto trae
    /// <c>RolSugeridoId</c>.
    /// </summary>
    internal static async Task<Organizacion> CrearOrganizacionAsync(HttpClient client, bool rolSugerido = false)
    {
        var rolId = await LocalizarRolNoSuperAdminAsync(client);

        var sucursalClave = RandomClave("SCB");
        var sucursal = await client.PostAsJsonAsync($"{EmpresasBase}/sucursales", new
        {
            Id = Guid.Empty, Clave = sucursalClave, Nombre = $"Sucursal {sucursalClave}",
        });
        sucursal.EnsureSuccessStatusCode();
        var sucursalId = (await ReadJsonAsync(sucursal)).GetProperty("id").GetGuid();

        var deptoClave = RandomClave("DCB");
        var depto = await client.PostAsJsonAsync("/api/v1/admin/departamentos", new
        {
            Id = Guid.Empty, Clave = deptoClave, Nombre = $"Departamento {deptoClave}",
        });
        depto.EnsureSuccessStatusCode();
        var deptoId = (await ReadJsonAsync(depto)).GetProperty("id").GetGuid();
        (await client.PostAsync($"{EmpresasBase}/sucursales/{sucursalId}/departamentos/{deptoId}", null))
            .EnsureSuccessStatusCode();

        var puestoClave = RandomClave("PCB");
        var puesto = await client.PostAsJsonAsync("/api/v1/admin/puestos", new
        {
            Id = Guid.Empty, Clave = puestoClave, Nombre = $"Puesto {puestoClave}",
            RolSugeridoId = rolSugerido ? rolId : (Guid?)null,
        });
        puesto.EnsureSuccessStatusCode();
        var puestoId = (await ReadJsonAsync(puesto)).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync(
            $"{EmpresasBase}/sucursales/{sucursalId}/puestos/{puestoId}",
            new { DepartamentoId = deptoId })).EnsureSuccessStatusCode();

        return new Organizacion(sucursalId, deptoId, puestoId, rolId);
    }

    /// <summary>Cuenta nueva en el directorio simulado (singleton del host de tests).</summary>
    private async Task<CuentaEntra> CrearCuentaEntraSimuladaAsync()
    {
        var directorio = _factory.Services.GetRequiredService<IEntraDirectorioPort>();
        var upn = $"colab-{RandomSufijo()}@millet.mx";
        var creada = await directorio.CrearCuentaAsync(
            new SolicitudCuentaEntra(upn, "Colaborador Simulado", "contacto@gmail.com", upn),
            CancellationToken.None);
        return creada.Cuenta;
    }

    private static async Task<Guid> LocalizarRolNoSuperAdminAsync(HttpClient client)
    {
        var rolesResp = await client.GetAsync("/api/v1/identidad/roles?limit=200");
        rolesResp.EnsureSuccessStatusCode();
        foreach (var r in (await ReadJsonAsync(rolesResp)).GetProperty("items").EnumerateArray())
        {
            if (r.GetProperty("codigo").GetString() != "super-admin")
                return r.GetProperty("id").GetGuid();
        }
        throw new InvalidOperationException("No hay roles distintos de super-admin.");
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"Esperado {status}, fue {response.StatusCode}: {raw}");
        Assert.Contains(code, raw);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    internal static string RandomSufijo() => Guid.NewGuid().ToString("N")[..8];

    internal static string RandomClave(string prefix) => $"{prefix}-{RandomSufijo().ToUpperInvariant()}";

    internal static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
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

    internal static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
