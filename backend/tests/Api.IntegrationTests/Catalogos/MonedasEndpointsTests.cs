using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Catalogos;

/// <summary>
/// Tests integration de los endpoints de Monedas + TiposCambio
/// (F-Admin-PR5.1). Cubre POST + PATCH + registro de tipo de cambio
/// + duplicado (409).
/// </summary>
public class MonedasEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private readonly WebApplicationFactory<Program> _factory;

    public MonedasEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListarMonedas_Con_Token_Retorna_OK_Con_Seed()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync("/api/v1/catalogos/monedas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await ReadJsonArrayAsync(response);
        Assert.True(items.GetArrayLength() >= 12, "Seed inicial de 12 monedas.");
    }

    [Fact]
    public async Task CrearMoneda_Retorna_201_Con_Codigo_Y_Id()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomMonedaCodigo();

        var response = await client.PostAsJsonAsync("/api/v1/catalogos/monedas", new
        {
            Codigo = codigo,
            Nombre = "Moneda Test " + codigo,
            Decimales = 2,
            Activa = true,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(codigo, body.GetProperty("codigo").GetString());
        Assert.True(body.GetProperty("activa").GetBoolean());
    }

    [Fact]
    public async Task CrearMoneda_Con_Codigo_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();

        // MXN ya existe en el seed.
        var response = await client.PostAsJsonAsync("/api/v1/catalogos/monedas", new
        {
            Codigo = "MXN",
            Nombre = "Peso Mexicano duplicado",
            Decimales = 2,
            Activa = true,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PatchMoneda_Cambia_Nombre_Y_Get_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();
        var codigo = RandomMonedaCodigo();

        // Crear
        var created = await client.PostAsJsonAsync("/api/v1/catalogos/monedas", new
        {
            Codigo = codigo, Nombre = "Original", Decimales = 2, Activa = true,
        });
        created.EnsureSuccessStatusCode();
        var createdBody = await ReadJsonAsync(created);
        var id = createdBody.GetProperty("id").GetGuid();

        // PATCH nombre
        var patched = await client.PatchAsJsonAsync($"/api/v1/catalogos/monedas/{id}", new
        {
            Nombre = "Nombre Actualizado",
            Decimales = (int?)null,
            Activa = (bool?)null,
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        var patchedBody = await ReadJsonAsync(patched);
        Assert.Equal("Nombre Actualizado", patchedBody.GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task RegistrarTipoCambio_Retorna_201_Y_Duplicado_409()
    {
        var client = await CreateSuperAdminClientAsync();

        // Crear moneda primero
        var codigo = RandomMonedaCodigo();
        var created = await client.PostAsJsonAsync("/api/v1/catalogos/monedas", new
        {
            Codigo = codigo, Nombre = "Test TC", Decimales = 2, Activa = true,
        });
        created.EnsureSuccessStatusCode();
        var createdBody = await ReadJsonAsync(created);
        var monedaId = createdBody.GetProperty("id").GetGuid();

        var fecha = new DateOnly(2026, 5, 14);
        var tcResp1 = await client.PostAsJsonAsync(
            $"/api/v1/catalogos/monedas/{monedaId}/tipos-cambio", new
            {
                Fecha = fecha,
                ValorEnMxn = 18.5m,
                Origen = (int?)null,
            });
        Assert.Equal(HttpStatusCode.Created, tcResp1.StatusCode);

        // Duplicado (misma moneda + fecha) → 409
        var tcResp2 = await client.PostAsJsonAsync(
            $"/api/v1/catalogos/monedas/{monedaId}/tipos-cambio", new
            {
                Fecha = fecha,
                ValorEnMxn = 18.7m,
                Origen = (int?)null,
            });
        Assert.Equal(HttpStatusCode.Conflict, tcResp2.StatusCode);
    }

    // --- Helpers ---

    private static string RandomMonedaCodigo()
    {
        // 3 letras random (evita choque con catalogo SAT seed). Empieza con Z para minimizar.
        var rnd = new Random();
        var letras = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return $"Z{letras[rnd.Next(letras.Length)]}{letras[rnd.Next(letras.Length)]}";
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

    private static async Task<JsonElement> ReadJsonArrayAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
