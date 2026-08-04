using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;

using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.Bifurcacion;

/// <summary>
/// Tests integration (F4-PR4) que cubren autorización de dos niveles
/// (matriz <c>RequiereN1YN2</c>): después de N1, la RQ sigue en
/// <c>EnAutorizacion</c> sin bifurcación; después de N2, la matriz
/// queda satisfecha y se ejecuta la bifurcación stock-aware.
///
/// <para>
/// Override <see cref="IRequiereNivelEvaluator"/> con
/// <see cref="AlwaysN1YN2Evaluator"/> vía <c>ConfigureTestServices</c>
/// para forzar el escenario, sin depender de configurar
/// <c>compras.umbrales_aprobacion_departamento</c> (datos compartidos).
/// </para>
/// </summary>
public class TwoLevelAuthEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public TwoLevelAuthEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DosNiveles_N1MantieneEnAutorizacion_N2TransicionaA_EnSurtido()
    {
        await using var twoLevelFactory = WithTwoLevelEvaluator();
        var client = await CreateSuperAdminClientAsync(twoLevelFactory);
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSeedId, cantidad: 10m);

        // Primera autorización N1: matriz no satisfecha → estado sigue
        // en EnAutorizacion, sin bifurcación (no se invocan stocks/oc).
        var n1 = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        n1.EnsureSuccessStatusCode();

        var afterN1 = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        Assert.Equal((int)EstadoRequisicion.EnAutorizacion, afterN1.GetProperty("estado").GetInt32());

        // N2: matriz satisfecha → bifurcación + cubrimiento. ADR-0043 #3: el
        // cubrimiento YA NO cierra (ni el path 100% stock) → queda EnSurtido.
        var n2 = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 2, Notas = (string?)null });
        n2.EnsureSuccessStatusCode();

        var afterN2 = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        Assert.Equal((int)EstadoRequisicion.EnSurtido, afterN2.GetProperty("estado").GetInt32());
    }

    [Fact]
    public async Task DosNiveles_StockCero_N2TransicionaA_EnSurtido()
    {
        await using var twoLevelFactory = WithTwoLevelEvaluator();
        var client = await CreateSuperAdminClientAsync(twoLevelFactory);
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var n1 = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        n1.EnsureSuccessStatusCode();

        var afterN1 = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        Assert.Equal((int)EstadoRequisicion.EnAutorizacion, afterN1.GetProperty("estado").GetInt32());

        var n2 = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 2, Notas = (string?)null });
        n2.EnsureSuccessStatusCode();

        var afterN2 = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        Assert.Equal((int)EstadoRequisicion.EnSurtido, afterN2.GetProperty("estado").GetInt32());
    }

    private WebApplicationFactory<Program> WithTwoLevelEvaluator() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequiereNivelEvaluator>();
                services.AddScoped<IRequiereNivelEvaluator, AlwaysN1YN2Evaluator>();
            });
        });

    private sealed class AlwaysN1YN2Evaluator : IRequiereNivelEvaluator
    {
        public Task<RequiereNivel> EvaluarAsync(Requisicion requisicion, CancellationToken cancellationToken = default) =>
            Task.FromResult(RequiereNivel.N1YN2);
    }

    // --- Helpers ---

    private static async Task<HttpClient> CreateSuperAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearTransmitirAsync(HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration F4-PR4 dos niveles");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", rqBody);
        crear.EnsureSuccessStatusCode();
        var rqId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var lineaBody = new
        {
            ArticuloId = articuloId,
            Cantidad = cantidad,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)null,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var linea = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
        linea.EnsureSuccessStatusCode();

        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();
        return rqId;
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
        var json = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.Clone();
    }
}
