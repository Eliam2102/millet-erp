using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration de los endpoints de parámetros globales del sistema
/// (F-Admin-PR7.1). Los 4 seeds default (TimezoneDefault, FormatoFecha,
/// RedondeoMonetario, IdiomaDefault) llegan de la migración
/// <c>20260514175731_ParametrosGlobales</c>.
/// </summary>
public class ParametrosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/parametros";

    private readonly WebApplicationFactory<Program> _factory;

    public ParametrosEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Con_SuperAdmin_Retorna_Seeds_Default()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync(EndpointBase);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        var items = body.GetProperty("items").EnumerateArray().ToList();

        // Los 4 seeds default deben estar presentes.
        var claves = items.Select(i => i.GetProperty("clave").GetString()).ToHashSet();
        Assert.Contains("system.timezone-default", claves);
        Assert.Contains("system.formato-fecha", claves);
        Assert.Contains("system.redondeo-monetario", claves);
        Assert.Contains("system.idioma-default", claves);
    }

    [Fact]
    public async Task Patch_Valor_Valido_Actualiza_Y_Get_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();

        // Lee valor original.
        var initial = await client.GetAsync(EndpointBase);
        var initialBody = await ReadJsonAsync(initial);
        var fecha = initialBody.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("clave").GetString() == "system.formato-fecha");
        var valorOriginal = fecha.GetProperty("valor").GetString();

        // PATCH a nuevo valor.
        var patch = await client.PatchAsJsonAsync(
            $"{EndpointBase}/system.formato-fecha",
            new { Valor = "yyyy-MM-dd" });
        patch.EnsureSuccessStatusCode();
        var patchBody = await ReadJsonAsync(patch);
        Assert.Equal("yyyy-MM-dd", patchBody.GetProperty("valor").GetString());

        // Restaurar para no contaminar otros tests.
        var restore = await client.PatchAsJsonAsync(
            $"{EndpointBase}/system.formato-fecha",
            new { Valor = valorOriginal });
        restore.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Patch_Valor_Invalido_Para_Numero_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();

        // system.redondeo-monetario es TipoParametro.Numero; un string no parsea.
        var response = await client.PatchAsJsonAsync(
            $"{EndpointBase}/system.redondeo-monetario",
            new { Valor = "no-es-numero" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Clave_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.PatchAsJsonAsync(
            $"{EndpointBase}/no.existe.parametro",
            new { Valor = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
