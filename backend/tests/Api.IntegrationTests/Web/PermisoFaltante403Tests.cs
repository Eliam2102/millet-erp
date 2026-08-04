using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Web;

/// <summary>
/// Tests integration de <c>PermisoFaltanteResultHandler</c>: el 403 del
/// middleware de autorización (policy <c>permiso:*</c> no satisfecha) debe
/// salir como Problem Details con <c>code=PERMISO_FALTANTE</c> y el permiso
/// canónico faltante en <c>detail</c> y <c>permisosFaltantes[]</c> — antes
/// respondía body vacío y el frontend mostraba "HTTP 403" sin pista.
/// </summary>
public class PermisoFaltante403Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SinPermisosOid = "test-no-perms";

    private readonly WebApplicationFactory<Program> _factory;

    public PermisoFaltante403Tests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Sin_Permiso_Retorna_ProblemDetails_Con_Permiso_Faltante()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Cualquier endpoint con RequireAuthorization("permiso:...") sirve;
        // la bandeja de CFDIs exige cuentas_por_pagar.cfdis.leer.
        var response = await client.GetAsync("/api/v1/cuentas-por-pagar/cfdis");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await ReadJsonAsync(response);
        Assert.Equal("PERMISO_FALTANTE", body.GetProperty("code").GetString());
        Assert.Equal(403, body.GetProperty("status").GetInt32());
        Assert.Contains("cuentas_por_pagar.cfdis.leer", body.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));

        var permisos = body.GetProperty("permisosFaltantes")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Contains("cuentas_por_pagar.cfdis.leer", permisos);
    }

    [Fact]
    public async Task Get_Sin_Token_Sigue_Retornando_401()
    {
        // El handler solo intercepta Forbidden; el challenge (401) delega
        // al default de ASP.NET y no debe cambiar.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/cuentas-por-pagar/cfdis");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Helpers ---

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
