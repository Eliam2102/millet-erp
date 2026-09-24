using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del endpoint consolidado de auditoría
/// <c>GET /api/v1/admin/auditoria</c> (F-Admin-PR7.2). Cierra
/// <c>PLATFORM-TODO(&lt;AuditUI&gt;)</c>.
/// </summary>
public class AuditoriaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/auditoria";

    private readonly WebApplicationFactory<Program> _factory;

    public AuditoriaEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Sin_Rango_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        // ASP.NET model binder rechaza con 400 cuando falta un required [FromQuery] DateOnly.
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Con_Rango_Excediendo_90_Dias_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var hace100 = hoy.AddDays(-100);
        var url = $"{EndpointBase}?desde={hace100:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}";

        var response = await client.GetAsync(url);

        // ValidationPipelineBehavior + GlobalExceptionHandler → 400
        // (convención del repo: validators = 400, invariantes domain = 422).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Con_Rango_Valido_Retorna_200_Con_Shape_Esperado()
    {
        var client = await CreateSuperAdminClientAsync();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var hace30 = hoy.AddDays(-30);
        var url = $"{EndpointBase}?desde={hace30:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}";

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(body.TryGetProperty("total", out var total));
        Assert.True(total.GetInt32() >= 0);
    }

    [Fact]
    public async Task Get_Filtra_Sucursal_Sin_Ocultar_Eventos_Globales_En_Consulta_Sin_Filtro()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"AUD-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var crear = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        crear.EnsureSuccessStatusCode();
        var sucursalId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var desde = hoy.AddDays(-1);
        var url = $"{EndpointBase}?desde={desde:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&sucursalId={sucursalId}";

        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await ReadJsonAsync(response)).GetProperty("items");
        Assert.Contains(items.EnumerateArray(), i =>
            i.GetProperty("entidad").GetString() == "Sucursal" &&
            i.GetProperty("sucursalId").GetGuid() == sucursalId &&
            i.GetProperty("sucursalClave").GetString() == clave);
        Assert.All(items.EnumerateArray(), i =>
            Assert.Equal(sucursalId, i.GetProperty("sucursalId").GetGuid()));

        var global = await client.GetAsync(
            $"{EndpointBase}?desde={desde:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, global.StatusCode);
    }

    [Fact]
    public async Task Get_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var hace30 = hoy.AddDays(-30);
        var response = await client.GetAsync(
            $"{EndpointBase}?desde={hace30:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
