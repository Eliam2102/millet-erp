using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.IntegrationTests.Fixtures;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de B.4 (PATCH cabecera) y B.2 (GET historico)
/// agrupados — comparten el flow "crear RQ + mutar + verificar".
/// </summary>
public class PatchYHistoricoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EndpointBase = "/api/v1/compras/requisiciones";
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public PatchYHistoricoTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --- B.4: PATCH cabecera ---

    [Fact]
    public async Task Patch_Cabecera_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{Guid.NewGuid()}",
            new { Descripcion = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Cabecera_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{Guid.NewGuid()}",
            new { Descripcion = "x" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Cabecera_Borrador_Cambia_Campos_Editables()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearBorradorAsync(client);

        var newFecha = new DateOnly(2026, 12, 31);
        var patchResp = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{rqId}",
            new
            {
                Descripcion = "descripción nueva",
                FechaEntregaDeseada = newFecha,
                Prioridad = 2,        // Alta
                Clasificacion = 1,    // Servicio
            });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        var detailResp = await client.GetAsync($"{EndpointBase}/{rqId}");
        var body = await ReadJsonAsync(detailResp);
        Assert.Equal("descripción nueva", body.GetProperty("descripcion").GetString());
        Assert.Equal(newFecha, body.GetProperty("fechaEntregaDeseada").GetDateTime().Date.ToDateOnly());
        Assert.Equal(2, body.GetProperty("prioridad").GetInt32());
        Assert.Equal(1, body.GetProperty("clasificacion").GetInt32());
    }

    [Fact]
    public async Task Patch_Cabecera_Limpiar_Descripcion_Setea_Null()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearBorradorAsync(client);

        var patchResp = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{rqId}",
            new { LimpiarDescripcion = true });
        patchResp.EnsureSuccessStatusCode();

        var detailResp = await client.GetAsync($"{EndpointBase}/{rqId}");
        var body = await ReadJsonAsync(detailResp);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("descripcion").ValueKind);
    }

    [Fact]
    public async Task Patch_Cabecera_En_EnAutorizacion_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearBorradorAsync(client);
        await AgregarLineaAsync(client, rqId);
        await TransmitirAsync(client, rqId);

        var response = await client.PatchAsJsonAsync(
            $"{EndpointBase}/{rqId}",
            new { Descripcion = "ya muy tarde" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("EDITAR_CABECERA_SOLO_EN_BORRADOR", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Patch_Cabecera_Sin_IdempotencyKey_Retorna_400()
    {
        var rawClient = _factory.CreateClient();
        var token = await FakeLoginAsync(rawClient, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        rawClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await rawClient.PatchAsJsonAsync(
            $"{EndpointBase}/{Guid.NewGuid()}",
            new { Descripcion = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", body.GetProperty("code").GetString());
    }

    // --- B.2: GET historico ---

    [Fact]
    public async Task Historico_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"{EndpointBase}/{Guid.NewGuid()}/historico");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Historico_RqInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync($"{EndpointBase}/{Guid.NewGuid()}/historico");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Historico_RqNueva_Trae_Al_Menos_Entry_Creada()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearBorradorAsync(client);

        var response = await client.GetAsync($"{EndpointBase}/{rqId}/historico");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var items = body.EnumerateArray().ToList();
        Assert.NotEmpty(items);
        // El interceptor escribe la entry de creación con tipo Creada.
        var creada = items.FirstOrDefault(i =>
            i.GetProperty("entidad").GetString() == "Requisicion"
            && i.GetProperty("operacion").GetString() == "crear");
        Assert.NotEqual(JsonValueKind.Undefined, creada.ValueKind);
        Assert.Equal((int)Millet.Compras.Application.Historico.HistoricoTipo.Creada, creada.GetProperty("tipo").GetInt32());
    }

    [Fact]
    public async Task Historico_AgregarLinea_Aparece_Como_LineaAgregada()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearBorradorAsync(client);
        await AgregarLineaAsync(client, rqId);

        var response = await client.GetAsync($"{EndpointBase}/{rqId}/historico");
        var body = await ReadJsonAsync(response);
        var items = body.EnumerateArray().ToList();
        var lineaAgregada = items.FirstOrDefault(i =>
            i.GetProperty("entidad").GetString() == "LineaRequisicion"
            && i.GetProperty("operacion").GetString() == "crear");
        Assert.NotEqual(JsonValueKind.Undefined, lineaAgregada.ValueKind);
        Assert.Equal(
            (int)Millet.Compras.Application.Historico.HistoricoTipo.LineaAgregada,
            lineaAgregada.GetProperty("tipo").GetInt32());
    }

    [Fact]
    public async Task Historico_Transmitir_Aparece_Como_Transmitida()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearBorradorAsync(client);
        await AgregarLineaAsync(client, rqId);
        await TransmitirAsync(client, rqId);

        var response = await client.GetAsync($"{EndpointBase}/{rqId}/historico");
        var body = await ReadJsonAsync(response);
        var items = body.EnumerateArray().ToList();
        var transmitida = items.FirstOrDefault(i =>
            i.GetProperty("tipo").GetInt32() == (int)Millet.Compras.Application.Historico.HistoricoTipo.Transmitida);
        Assert.NotEqual(JsonValueKind.Undefined, transmitida.ValueKind);
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearBorradorAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync(
            EndpointBase,
            TestComprasFixtures.BuildCrearRqValidBody(descripcion: "test B.2/B.4"));
        resp.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(resp);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task AgregarLineaAsync(HttpClient client, Guid rqId)
    {
        var resp = await client.PostAsJsonAsync(
            $"{EndpointBase}/{rqId}/lineas",
            new
            {
                ArticuloId = ArticuloSeedId,
                Cantidad = 5m,
                UnidadMedida = "PZA",
                PrecioEstimadoMonto = 100m,
                PrecioEstimadoMoneda = "MXN",
                CuentaContableId = (Guid?)null,
                CentroCostoId = (Guid?)Guid.Parse("0000000c-0005-0000-0000-000000000001"), // Fase E PR2.1: CC obligatorio
                Proyecto = (string?)null,
                FechaRequerida = (DateOnly?)null,
                Notas = (string?)null,
            });
        resp.EnsureSuccessStatusCode();
    }

    private static async Task TransmitirAsync(HttpClient client, Guid rqId)
    {
        var resp = await client.PostAsync($"{EndpointBase}/{rqId}/transmitir", content: null);
        resp.EnsureSuccessStatusCode();
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

internal static class DateTimeExtensions
{
    public static DateOnly ToDateOnly(this DateTime dt) => DateOnly.FromDateTime(dt);
}
