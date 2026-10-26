using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.Auth.Models;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// Cajas por sucursal (86e3t97uy): la caja nueva pide sucursal; sin ella el
/// endpoint responde error de validación en lugar de crearla visible para todas.
/// </summary>
public sealed class CajasSucursalEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Crear_caja_sin_sucursal_devuelve_error_de_validacion()
    {
        var client = await ClienteAutenticadoAsync();

        var response = await client.PostAsJsonAsync("/api/v1/facturacion/cajas/",
            new { Nombre = $"Caja sin sucursal {Guid.NewGuid():N}", Descripcion = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("errores").EnumerateArray(),
            e => e.GetProperty("field").GetString() == "SucursalId");
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
