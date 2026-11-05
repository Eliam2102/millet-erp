using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-ALM-15: <c>GET /api/v1/almacen/reportes/kardex</c> corre la consulta real contra Postgres (traducción EF del
/// kardex con ubicación) y valida el periodo. Solo status y shape, robusto a datos.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class KardexEndpointTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public async Task Articulo_y_ubicacion_retorna_200_con_shape_del_reporte()
    {
        var client = await ClienteAsync();

        var response = await client.GetAsync($"/api/v1/almacen/reportes/kardex?articuloId={Guid.NewGuid()}&desde=2027-01-01&hasta=2027-01-31&ubicacionId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("columnas", out _));
        Assert.Equal(1, body.GetProperty("filas").GetArrayLength()); // solo «Saldo inicial»
        Assert.Equal(0m, body.GetProperty("totales").GetProperty("saldo").GetDecimal());
    }

    [Fact]
    public async Task Periodo_invertido_retorna_400()
    {
        var client = await ClienteAsync();

        var response = await client.GetAsync($"/api/v1/almacen/reportes/kardex?articuloId={Guid.NewGuid()}&desde=2027-02-01&hasta=2027-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> ClienteAsync()
    {
        var client = factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev", EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
