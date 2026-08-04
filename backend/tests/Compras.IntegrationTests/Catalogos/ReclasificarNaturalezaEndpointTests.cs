using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.IntegrationTests.Catalogos;

/// <summary>
/// Tests integration del endpoint POST /api/v1/catalogos/articulos/reclasificar-naturaleza
/// (F9-PR1). Usa los artículos seedeados por <c>CatalogosTestSeedHostedService</c>;
/// reclasifica un par de IDs y verifica vía GET que la naturaleza cambió.
/// Restaura el estado al final para no contaminar los demás tests.
/// </summary>
public class ReclasificarNaturalezaEndpointTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";

    // Magic GUIDs del stub de stock (ART-TEST-AAA / ART-TEST-BBB):
    // existen en el seed con Naturaleza=Estandar y NINGÚN otro test asserta
    // su naturaleza — los stubs los reconocen por ID, no por enum. Esto
    // los hace seguros para mutar y restaurar sin contaminar otros tests
    // que corren en paralelo contra la misma BD.
    private static readonly Guid ArticuloA = Guid.Parse("00000000-0000-0000-0000-000000000aaa");
    private static readonly Guid ArticuloB = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public ReclasificarNaturalezaEndpointTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reclasificar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos/reclasificar-naturaleza",
            new { ArticuloIds = new[] { ArticuloA }, Naturaleza = (int)Naturaleza.Critico, Motivo = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reclasificar_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos/reclasificar-naturaleza",
            new { ArticuloIds = new[] { ArticuloA }, Naturaleza = (int)Naturaleza.Critico });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reclasificar_Happy_Path_Cambia_Y_Devuelve_Conteo()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos/reclasificar-naturaleza",
            new
            {
                ArticuloIds = new[] { ArticuloA, ArticuloB },
                Naturaleza = (int)Naturaleza.Critico,
                Motivo = "test reclasificar",
            });

        try
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await ReadJsonAsync(response);
            Assert.Equal(2, json.GetProperty("reclasificados").GetInt32());

            // Verificar via GET detalle.
            var detalle = await client.GetAsync($"/api/v1/catalogos/articulos/{ArticuloA}");
            var detalleJson = await ReadJsonAsync(detalle);
            Assert.Equal((int)Naturaleza.Critico, detalleJson.GetProperty("naturaleza").GetInt32());
        }
        finally
        {
            // Restaurar.
            await client.PostAsJsonAsync(
                "/api/v1/catalogos/articulos/reclasificar-naturaleza",
                new
                {
                    ArticuloIds = new[] { ArticuloA, ArticuloB },
                    Naturaleza = (int)Naturaleza.Estandar,
                    Motivo = "restore test",
                });
        }
    }

    [Fact]
    public async Task Reclasificar_Sin_Articulo_Ids_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos/reclasificar-naturaleza",
            new { ArticuloIds = Array.Empty<Guid>(), Naturaleza = (int)Naturaleza.Critico });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reclasificar_Batch_Demasiado_Grande_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var ids = Enumerable.Range(0, 501).Select(_ => Guid.CreateVersion7()).ToArray();

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos/reclasificar-naturaleza",
            new { ArticuloIds = ids, Naturaleza = (int)Naturaleza.Critico });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("RECLASIFICAR_BATCH_DEMASIADO_GRANDE", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reclasificar_Sin_IdempotencyKey_Retorna_400()
    {
        var rawClient = _factory.CreateClient();
        var token = await FakeLoginAsync(rawClient, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        rawClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await rawClient.PostAsJsonAsync(
            "/api/v1/catalogos/articulos/reclasificar-naturaleza",
            new { ArticuloIds = new[] { ArticuloA }, Naturaleza = (int)Naturaleza.Critico });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
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
