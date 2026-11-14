using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// H-U35: dar de alta un vehículo con una placa ya registrada (o un operador con un RFC ya registrado) respondía 500 por el
/// índice único; ahora responde 409 con un código y un mensaje claros. Todos los datos son ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CartaPorteCatalogoDuplicadosTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public async Task Una_placa_repetida_responde_409_con_mensaje_claro()
    {
        using var client = await Login();
        var placa = $"hu{Guid.NewGuid().ToString("N")[..6]}";
        var ids = new List<Guid>();
        try
        {
            var primera = await Post(client, "/api/v1/facturacion/carta-porte/vehiculos",
                new { placa, configVehicular = "C2", anioModelo = 2022, pesoBrutoVehicular = 17.5m });
            Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
            ids.Add((await primera.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

            // Misma placa en mayúsculas: el catálogo la guarda normalizada, así que también es repetida.
            var repetida = await Post(client, "/api/v1/facturacion/carta-porte/vehiculos",
                new { placa = placa.ToUpperInvariant(), configVehicular = "C2", anioModelo = 2023 });

            Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
            var problema = await repetida.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("FAC_CP_VEHICULO_PLACA_DUPLICADA", problema.GetProperty("code").GetString());
            Assert.Contains(placa.ToUpperInvariant(), problema.GetProperty("detail").GetString());
        }
        finally
        {
            await Limpiar(db => db.Vehiculos.Where(v => ids.Contains(v.Id)).ExecuteDeleteAsync());
        }
    }

    [Fact]
    public async Task Un_rfc_de_operador_repetido_responde_409_con_mensaje_claro()
    {
        using var client = await Login();
        var rfc = $"XAXX{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var ids = new List<Guid>();
        try
        {
            var primero = await Post(client, "/api/v1/facturacion/carta-porte/operadores", new { rfc, nombre = "OPERADOR FICTICIO", numLicencia = "LIC-1" });
            Assert.Equal(HttpStatusCode.Created, primero.StatusCode);
            ids.Add((await primero.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

            var repetido = await Post(client, "/api/v1/facturacion/carta-porte/operadores", new { rfc, nombre = "OTRO OPERADOR", numLicencia = "LIC-2" });

            Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
            var problema = await repetido.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("FAC_CP_OPERADOR_RFC_DUPLICADO", problema.GetProperty("code").GetString());
        }
        finally
        {
            await Limpiar(db => db.Operadores.Where(o => ids.Contains(o.Id)).ExecuteDeleteAsync());
        }
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private async Task Limpiar(Func<FacturacionDbContext, Task<int>> borrar)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        await borrar(scope.ServiceProvider.GetRequiredService<FacturacionDbContext>());
    }

    private async Task<HttpClient> Login()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }
}
