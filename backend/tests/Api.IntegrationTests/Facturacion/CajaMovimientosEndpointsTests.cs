using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.Auth.Models;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// Motivo obligatorio del retiro de efectivo (86e3tfdwa): el endpoint rechaza
/// un retiro con motivo de menos de 15 caracteres antes de tocar la sesión.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CajaMovimientosEndpointsTests(WebApplicationFactory<Program> factory)
{
    private const int TipoRetiro = 3;

    [Fact]
    public async Task Retiro_con_motivo_de_menos_de_15_caracteres_devuelve_error_de_validacion()
    {
        var client = await ClienteAutenticadoAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/facturacion/cajas/sesiones/{Guid.NewGuid()}/movimientos",
            new { Tipo = TipoRetiro, FormaPago = "01", Importe = 100m, Descripcion = "Camión taller" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("errores").EnumerateArray(),
            e => e.GetProperty("field").GetString() == "Descripcion");
    }

    [Fact]
    public async Task Retiro_con_motivo_de_15_caracteres_pasa_la_validacion()
    {
        var client = await ClienteAutenticadoAsync();

        // La sesión no existe: si el motivo pasa la validación, el handler responde 404.
        var response = await client.PostAsJsonAsync(
            $"/api/v1/facturacion/cajas/sesiones/{Guid.NewGuid()}/movimientos",
            new { Tipo = TipoRetiro, FormaPago = "01", Importe = 100m, Descripcion = "Reparación 1234" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> ClienteAutenticadoAsync()
    {
        var client = factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev" });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }
}
