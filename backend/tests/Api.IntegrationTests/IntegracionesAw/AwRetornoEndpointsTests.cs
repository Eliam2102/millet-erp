using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

/// <summary>M-160: resumen de avisos a A+W pendientes y reproceso de dead-letter (permiso de configuración de Integraciones.Aw).</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class AwRetornoEndpointsTests(WebApplicationFactory<Program> factory)
{
    private const string Resumen = "/api/v1/integraciones/aw/retorno/pendientes/resumen";
    private const string Reprocesar = "/api/v1/integraciones/aw/retorno/dead-letter/reprocesar";

    private async Task<HttpClient> Cliente(bool admin)
    {
        var client = factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = admin ? "dev-superadmin" : "dev-aw-retorno-sin-permiso",
            Email = admin ? "superadmin@dev.local" : "aw-retorno@demo.invalid", Nombre = "Prueba retorno A+W", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Admin_ve_el_resumen_de_pendientes()
    {
        using var client = await Cliente(admin: true);
        var r = await client.GetAsync(Resumen);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pendientes").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        body.TryGetProperty("alerta", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Sin_permiso_no_ve_el_resumen_ni_reprocesa()
    {
        using var client = await Cliente(admin: false);
        (await client.GetAsync(Resumen)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(Reprocesar, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reprocesar_sin_Service_Bus_responde_422()
    {
        using var client = await Cliente(admin: true);
        var r = await client.PostAsync(Reprocesar, null);
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("SERVICE_BUS_NO_CONFIGURADO");
    }
}
