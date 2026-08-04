using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.IntegrationTests.Fixtures;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de <c>POST /requisiciones/{id}/rechazar</c> y
/// <c>POST /requisiciones/{id}/eliminar</c> (F2-PR4). Cubre transiciones
/// hacia <c>Rechazada</c> y <c>Eliminada</c> + validación cross-table de
/// motivos.
///
/// Motivos seed: todos con <c>aplica_a = 7</c> (Rechazo|Eliminacion|Cancelacion).
/// <c>RECH-DUP</c> tiene <c>permite_texto_libre = false</c>;
/// <c>RECH-OTRO</c> tiene <c>permite_texto_libre = true</c>.
/// </summary>
public class RechazarEliminarEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid MotivoSinTextoLibreId = Guid.Parse("00000003-0002-0000-0000-000000000001"); // RECH-DUP
    private static readonly Guid MotivoConTextoLibreId = Guid.Parse("00000003-0002-0000-0000-000000000006"); // RECH-OTRO

    private readonly WebApplicationFactory<Program> _factory;

    public RechazarEliminarEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --- POST /api/v1/compras/requisiciones/{id}/rechazar ---

    [Fact]
    public async Task Rechazar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/rechazar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rechazar_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/rechazar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rechazar_RqInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/rechazar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rechazar_DesdeBorrador_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/rechazar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("RECHAZAR_SOLO_DESDE_EN_AUTORIZACION", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rechazar_DesdeEnAutorizacion_Retorna_204_Y_Transiciona_A_Rechazada()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearYTransmitirAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/rechazar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        get.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(get);
        Assert.Equal(6, json.GetProperty("estado").GetInt32()); // Rechazada = 6
        Assert.Equal(MotivoSinTextoLibreId, json.GetProperty("motivoTerminacionId").GetGuid());
        Assert.NotEqual(Guid.Empty, json.GetProperty("actorTerminacionId").GetGuid());
    }

    [Fact]
    public async Task Rechazar_MotivoOtro_SinTexto_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearYTransmitirAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/rechazar",
            new { MotivoId = MotivoConTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("MOTIVO_TEXTO_REQUERIDO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rechazar_MotivoOtro_ConTexto_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearYTransmitirAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/rechazar",
            new { MotivoId = MotivoConTextoLibreId, MotivoTexto = "presupuesto agotado para este trimestre" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Rechazar_MotivoInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearYTransmitirAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/rechazar",
            new { MotivoId = Guid.NewGuid(), MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("MOTIVO_NO_ENCONTRADO", body.GetProperty("code").GetString());
    }

    // --- POST /api/v1/compras/requisiciones/{id}/eliminar ---

    [Fact]
    public async Task Eliminar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/eliminar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Eliminar_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/eliminar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Eliminar_DesdeBorrador_Retorna_204_Y_Transiciona_A_Eliminada()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/eliminar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        get.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(get);
        Assert.Equal(7, json.GetProperty("estado").GetInt32()); // Eliminada = 7
    }

    [Fact]
    public async Task Eliminar_DesdeEnAutorizacion_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearYTransmitirAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/eliminar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Eliminar_DesdeAutorizada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/eliminar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("ELIMINAR_SOLO_DESDE_BORRADOR_O_EN_AUTORIZACION", body.GetProperty("code").GetString());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearRequisicionAsync(HttpClient client)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration F2-PR4");
        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private static async Task AgregarLineaAsync(HttpClient client, Guid requisicionId)
    {
        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 10m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)Guid.Parse("0000000c-0005-0000-0000-000000000001"), // Fase E PR2.1: CC obligatorio
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            body);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CrearYTransmitirAsync(HttpClient client)
    {
        var rqId = await CrearRequisicionAsync(client);
        await AgregarLineaAsync(client, rqId);
        var transmitir = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir",
            content: null);
        transmitir.EnsureSuccessStatusCode();
        return rqId;
    }

    private static async Task<Guid> CrearTransmitirAutorizarAsync(HttpClient client)
    {
        var rqId = await CrearYTransmitirAsync(client);
        // Sin umbrales configurados, fail-open: una sola autorización N1
        // transiciona a Autorizada (mismo patrón que AutorizacionesEndpointsTests).
        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();
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
