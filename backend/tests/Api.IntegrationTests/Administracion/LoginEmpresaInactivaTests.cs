using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// F1-ADM-01 Fase 3 — pendientes #1 y #2 del incremento del 2026-09-21
/// (<c>fase-3-contexto-empresa.md</c>): confirma que una empresa desactivada
/// deja de ser seleccionable en login/cambio de empresa, y que una
/// <c>UsuarioPreferencia.UltimaEmpresaId</c> que quedó apuntando a una
/// empresa desactivada después no bloquea el siguiente login — el fallback
/// ya es automático porque <see cref="LoginOrchestrator"/> solo considera
/// empresas con <c>Activa = true</c> al resolver la selección, sin necesitar
/// limpiar la preferencia explícitamente.
/// </summary>
public class LoginEmpresaInactivaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EmpresasEndpointBase = "/api/v1/admin/empresas";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public LoginEmpresaInactivaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_Solicitando_EmpresaInactiva_Hace_Fallback_Y_La_Excluye_De_La_Lista()
    {
        var admin = await CreateSuperAdminClientAsync();
        var empresaBId = await CrearEmpresaActivaAsync(admin);
        var oid = await SeedUsuarioConEmpresasAsync(empresaBId);

        await DesactivarEmpresaAsync(admin, empresaBId);

        var (status, body) = await FakeLoginRawAsync(oid, $"{oid}@test.local", "Test Fallback", empresaBId);

        Assert.Equal(HttpStatusCode.OK, status);
        var empresas = body.GetProperty("empresas").EnumerateArray().ToList();
        Assert.DoesNotContain(empresas, e => e.GetProperty("id").GetGuid() == empresaBId);
        Assert.Contains(
            empresas,
            e => e.GetProperty("id").GetGuid() == EmpresaInicialId && e.GetProperty("esLaActual").GetBoolean());
    }

    [Fact]
    public async Task CambiarEmpresa_Hacia_EmpresaInactiva_Retorna_403()
    {
        var admin = await CreateSuperAdminClientAsync();
        var empresaBId = await CrearEmpresaActivaAsync(admin);
        var oid = await SeedUsuarioConEmpresasAsync(empresaBId);

        await DesactivarEmpresaAsync(admin, empresaBId);

        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Test 403");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/auth/cambiar-empresa", new { EmpresaId = empresaBId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("EMPRESA_ACCESS_DENIED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UltimaEmpresaId_Apuntando_A_Empresa_Desactivada_No_Bloquea_Login_Siguiente()
    {
        var admin = await CreateSuperAdminClientAsync();
        var empresaBId = await CrearEmpresaActivaAsync(admin);
        var oid = await SeedUsuarioConEmpresasAsync(empresaBId);

        // Primer login selecciona explícitamente Empresa B -> graba
        // UsuarioPreferencia.UltimaEmpresaId = empresaBId.
        var (status1, body1) = await FakeLoginRawAsync(oid, $"{oid}@test.local", "Test Ultima", empresaBId);
        Assert.Equal(HttpStatusCode.OK, status1);
        Assert.Contains(
            body1.GetProperty("empresas").EnumerateArray(),
            e => e.GetProperty("id").GetGuid() == empresaBId && e.GetProperty("esLaActual").GetBoolean());

        await DesactivarEmpresaAsync(admin, empresaBId);

        // Segundo login sin empresa solicitada explícita: debe ignorar la
        // UltimaEmpresaId ahora inactiva y caer al único activo restante,
        // sin lanzar error ni requerir limpieza previa de la preferencia.
        var (status2, body2) = await FakeLoginRawAsync(oid, $"{oid}@test.local", "Test Ultima", requestedEmpresaId: null);

        Assert.Equal(HttpStatusCode.OK, status2);
        var empresas2 = body2.GetProperty("empresas").EnumerateArray().ToList();
        Assert.Single(empresas2);
        Assert.Equal(EmpresaInicialId, empresas2[0].GetProperty("id").GetGuid());
        Assert.True(empresas2[0].GetProperty("esLaActual").GetBoolean());
    }

    // --- Helpers ---

    private static async Task<Guid> CrearEmpresaActivaAsync(HttpClient admin)
    {
        var rfc = RandomRfc();
        var response = await admin.PostAsJsonAsync(EmpresasEndpointBase, new
        {
            Id = Guid.Empty,
            Rfc = rfc,
            RazonSocial = "Empresa Fallback Test",
            RegimenFiscal = "601",
            NombreComercial = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task DesactivarEmpresaAsync(HttpClient admin, Guid empresaId)
    {
        var response = await admin.PostAsync($"{EmpresasEndpointBase}/{empresaId}/desactivar", content: null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Crea un usuario con asignación (rol sin permisos, irrelevante para
    /// estos tests que solo verifican selección de empresa) a la empresa
    /// bootstrap y a <paramref name="empresaBId"/>. Mismo patrón de seeding
    /// directo vía DbContext usado en <c>BandejasEndpointsTests</c>.
    /// </summary>
    private async Task<string> SeedUsuarioConEmpresasAsync(Guid empresaBId)
    {
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var random = Guid.NewGuid().ToString("N").Substring(0, 10).ToLowerInvariant();
        var rolId = Guid.CreateVersion7();
        identidad.Roles.Add(new Rol(
            rolId,
            $"test-fallback-{random}",
            "Test Fallback Empresa Inactiva",
            esDelSistema: false,
            "Rol sin permisos — solo para pruebas de selección de empresa."));

        var oid = $"test-fallback-{random}";
        var usuario = new Usuario(Guid.CreateVersion7(), oid, $"{oid}@test.local", "Usuario Fallback Test");
        identidad.Usuarios.Add(usuario);
        identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id));
        identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
            Guid.CreateVersion7(), usuario.Id, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
        identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
            Guid.CreateVersion7(), usuario.Id, empresaBId, rolId, asignadoPorUsuarioId: null));

        await identidad.SaveChangesAsync();
        return oid;
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> FakeLoginRawAsync(
        string oid, string email, string nombre, Guid? requestedEmpresaId)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = requestedEmpresaId,
        });
        var body = await ReadJsonAsync(response);
        return (response.StatusCode, body);
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

    private static string RandomRfc()
    {
        var rnd = new Random();
        var letras = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var prefix = new string(Enumerable.Range(0, 3).Select(_ => letras[rnd.Next(letras.Length)]).ToArray());
        var fecha = rnd.Next(100000, 999999).ToString();
        var sufijo = new string(Enumerable.Range(0, 3).Select(_ => letras[rnd.Next(letras.Length)]).ToArray());
        return $"{prefix}{fecha}{sufijo}";
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
