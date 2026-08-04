using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests integration del endpoint <c>SAP-REPORTE-EXISTENCIA-MP-CNK</c>
/// (<c>GET /api/v1/almacen/reportes/mp-cnk</c>). Regresión del 500 por
/// <c>InvalidOperationException</c>: el <c>OrderBy</c> sobre el record
/// proyectado (<c>select new ExistenciaMpCnkFila(...)</c>) no traducía en
/// EF Core 9.
///
/// <para>El bug es de <b>traducción de query</b> (independiente de datos),
/// así que solo lo caza un test que <b>ejecute la query real contra la BD</b>;
/// un unit en memoria pasaría aunque la traducción esté rota.</para>
///
/// <para>Estos casos assertan <b>solo status 200</b> + que el body deserializa
/// al shape del reporte (NO conteos ni valores) → robustos a datos, no tocan
/// el ruido frágil-por-datos de <c>millet_dev</c> poblada.</para>
/// </summary>
public class ReportesMpCnkEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private readonly WebApplicationFactory<Program> _factory;

    public ReportesMpCnkEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MpCnk_SinSubAlmacen_Todos_Retorna_200()
    {
        var client = await CreateSuperAdminClientAsync();

        // Caso "Todos" (sin subAlmacenId) — el del síntoma. Pre-fix: 500 por
        // OrderBy no traducible. Post-fix: 200.
        var response = await client.GetAsync("/api/v1/almacen/reportes/mp-cnk");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("columnas", out _));
        Assert.True(body.TryGetProperty("filas", out _));
    }

    [Fact]
    public async Task MpCnk_ConSubAlmacenId_Retorna_200()
    {
        var client = await CreateSuperAdminClientAsync();

        // subAlmacenId random: el endpoint no valida existencia → 200 con
        // reporte vacío. Ejercita el path CON filtro (mismo OrderBy → mismo bug
        // pre-fix). Cero dependencia de seed/datos.
        var response = await client.GetAsync(
            $"/api/v1/almacen/reportes/mp-cnk?subAlmacenId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("filas", out _));
    }

    // --- Helpers ---

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
