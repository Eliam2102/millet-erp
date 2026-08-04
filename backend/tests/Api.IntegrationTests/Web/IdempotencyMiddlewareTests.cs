using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Web;

/// <summary>
/// Tests integration del <c>IdempotencyMiddleware</c> (F8-PR1, ADR-0020).
/// Usa el endpoint dev <c>/api/dev/idempotent-echo</c> que está decorado
/// con <c>[RequireIdempotencyKey]</c> e incrementa un contador estático
/// — un replay correcto devuelve la misma respuesta cacheada (no incrementa).
/// </summary>
public class IdempotencyMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public IdempotencyMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Replay_MismaKey_MismoBody_DevuelveRespuestaCacheada()
    {
        var client = await CreateSuperAdminClientAsync();
        var key = NewIdempotencyKey();
        var payload = $"hola-{Guid.NewGuid():N}";

        var first = await PostEchoAsync(client, key, payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstJson = await ReadJsonAsync(first);
        var firstCount = firstJson.GetProperty("count").GetInt32();
        var firstPayload = firstJson.GetProperty("payload").GetString();

        // Replay con misma key + mismo body → respuesta cacheada (count idéntico).
        var second = await PostEchoAsync(client, key, payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(second.Headers.Contains("Idempotent-Replayed"));
        var secondJson = await ReadJsonAsync(second);
        Assert.Equal(firstCount, secondJson.GetProperty("count").GetInt32());
        Assert.Equal(firstPayload, secondJson.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task NuevaKey_IncrementaContador_NoEsCache()
    {
        var client = await CreateSuperAdminClientAsync();
        var payload = $"x-{Guid.NewGuid():N}";

        var first = await PostEchoAsync(client, NewIdempotencyKey(), payload);
        var second = await PostEchoAsync(client, NewIdempotencyKey(), payload);

        var firstCount = (await ReadJsonAsync(first)).GetProperty("count").GetInt32();
        var secondCount = (await ReadJsonAsync(second)).GetProperty("count").GetInt32();

        Assert.True(secondCount > firstCount, $"second={secondCount} debería ser > first={firstCount}");
        Assert.False(second.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task MismaKey_BodyDistinto_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var key = NewIdempotencyKey();

        var first = await PostEchoAsync(client, key, "payload-A");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostEchoAsync(client, key, "payload-DIFERENTE");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var json = await ReadJsonAsync(second);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SinHeader_EndpointDecorado_Retorna_400_MISSING()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/dev/idempotent-echo",
            new { Payload = "sin-key" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task HeaderInvalido_NoUuidV4_Retorna_400_INVALID()
    {
        var client = await CreateSuperAdminClientAsync();
        // UUID v1 (timestamp-based, byte 7 empieza con '1', no '4')
        const string uuidV1 = "550e8400-e29b-11d4-a716-446655440000";

        var response = await PostEchoAsync(client, uuidV1, "payload");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("INVALID_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SinAuth_Retorna_401_AntesDelMiddleware()
    {
        // Confirma orden del pipeline: Authentication corre ANTES del
        // IdempotencyMiddleware. Sin token, ni siquiera el header se evalúa.
        var client = _factory.CreateClient();

        var content = new StringContent(
            JsonSerializer.Serialize(new { Payload = "x" }),
            Encoding.UTF8,
            "application/json");
        content.Headers.Add("Idempotency-Key", NewIdempotencyKey());

        var response = await client.PostAsync("/api/dev/idempotent-echo", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EndpointSinDecoracion_SinHeader_FuncionaNormal()
    {
        // /api/dev/non-idempotent-echo NO está decorado con
        // [RequireIdempotencyKey]. Sin header el middleware pasa de largo
        // y el endpoint corre normal. Compras decoró todos sus POST en
        // F8-PR2 → ya no quedan POST de negocio sin decorar para usar aquí.
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/dev/non-idempotent-echo",
            new { Payload = "sin-decoracion" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<HttpResponseMessage> PostEchoAsync(HttpClient client, string idempotencyKey, string payload)
    {
        var content = new StringContent(
            JsonSerializer.Serialize(new { Payload = payload }),
            Encoding.UTF8,
            "application/json");
        content.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.PostAsync("/api/dev/idempotent-echo", content);
    }

    private static string NewIdempotencyKey() => Guid.NewGuid().ToString("D");

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
