using System.Net;
using System.Text.Json;
using Xunit;

namespace Millet.AwDropService.Tests;

// ============================================================================
// CompletionsEndpointTests
// Cubren GET /completions (reconciliación tardía) bajo el modelo de MARCADOR:
// enumera cot_<REF>.<docid> de Results\ por cursor mtime. Cada item lleva
// outcome="success" + aw_doc_id del nombre.
//   - Auth (X-API-Key faltante / incorrecta → 401)
//   - Empty → completions vacíos + next_since null
//   - Un marcador → outcome=success + aw_doc_id + filename=<ref>.edi
//   - Orden por mtime ASC + cursor since estricto + limit clamp
//   - Nombres inválidos ignorados
// ============================================================================

public class CompletionsEndpointTests
{
    private const string ApiKey = "test-api-key-32-bytes-aaaaaaaaaa";

    private static readonly DateTime T1 = new(2026, 5, 29, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 5, 29, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T3 = new(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Get_Completions_SinApiKey_Retorna401()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/completions");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Completions_ConApiKeyIncorrecta_Retorna401()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/completions");
        req.Headers.Add("X-API-Key", "wrong-key-same-length-aaaaaaaaaa");

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Completions_ResultsVacio_RetornaArrayVacioYNextSinceNull()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();
        var resp = await Get(client, "/completions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, root.GetProperty("completions").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("next_since").ValueKind);
    }

    [Fact]
    public async Task Get_Completions_UnMarcador_OutcomeSuccessYAwDocId()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();
        SeedMarker(factory, "cot_NIN_Q-2026-00346.31247", T1);

        var resp = await Get(client, "/completions");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var items = root.GetProperty("completions");
        Assert.Equal(1, items.GetArrayLength());
        var item = items[0];
        Assert.Equal("cot_NIN_Q-2026-00346.edi", item.GetProperty("filename").GetString());
        Assert.Equal("success", item.GetProperty("outcome").GetString());
        Assert.Equal(31247L, item.GetProperty("aw_doc_id").GetInt64());
        Assert.Equal("default", item.GetProperty("lane").GetString());

        // next_since == parsed_at del único item
        Assert.Equal(item.GetProperty("parsed_at").GetString(),
            root.GetProperty("next_since").GetString());
    }

    [Fact]
    public async Task Get_Completions_VariosMarcadores_OrdenadosPorMtimeAsc()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        // Sembrar en orden DESC de mtime para verificar que ordena ASC.
        SeedMarker(factory, "cot_C.3", T3);
        SeedMarker(factory, "cot_B.2", T2);
        SeedMarker(factory, "cot_A.1", T1);

        var resp = await Get(client, "/completions");
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var items = root.GetProperty("completions");
        Assert.Equal(3, items.GetArrayLength());

        var filenames = items.EnumerateArray()
            .Select(i => i.GetProperty("filename").GetString())
            .ToList();
        Assert.Equal(new[] { "cot_A.edi", "cot_B.edi", "cot_C.edi" }, filenames);
    }

    [Fact]
    public async Task Get_Completions_ConSince_DevuelveSoloPosteriores()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        SeedMarker(factory, "cot_A.1", T1);
        SeedMarker(factory, "cot_B.2", T2);
        SeedMarker(factory, "cot_C.3", T3);

        // since == mtime de A → devuelve B y C (estrictamente posterior)
        var resp = await Get(client, "/completions?since=2026-05-29T10:00:00.000Z");

        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var items = root.GetProperty("completions");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("cot_B.edi", items[0].GetProperty("filename").GetString());
        Assert.Equal("cot_C.edi", items[1].GetProperty("filename").GetString());
    }

    [Fact]
    public async Task Get_Completions_ConLimit_RespetaLimitYNextSinceApuntaAlUltimo()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        for (int i = 1; i <= 5; i++)
        {
            SeedMarker(factory, $"cot_{i:D2}.{i}", T1.AddMinutes(i));
        }

        var resp = await Get(client, "/completions?limit=2");
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        var items = root.GetProperty("completions");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("cot_01.edi", items[0].GetProperty("filename").GetString());
        Assert.Equal("cot_02.edi", items[1].GetProperty("filename").GetString());

        Assert.Equal(items[1].GetProperty("parsed_at").GetString(),
            root.GetProperty("next_since").GetString());
    }

    [Fact]
    public async Task Get_Completions_PaginacionCompleta_NoOverlap()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        for (int i = 1; i <= 4; i++)
        {
            SeedMarker(factory, $"cot_{i:D2}.{i}", T1.AddMinutes(i));
        }

        var page1 = JsonDocument.Parse(await (await Get(client, "/completions?limit=2"))
            .Content.ReadAsStringAsync()).RootElement;
        var nextSince = page1.GetProperty("next_since").GetString();
        Assert.NotNull(nextSince);

        var page2 = JsonDocument.Parse(await (await Get(client,
            $"/completions?since={Uri.EscapeDataString(nextSince!)}&limit=2"))
            .Content.ReadAsStringAsync()).RootElement;

        var page1Files = page1.GetProperty("completions").EnumerateArray()
            .Select(i => i.GetProperty("filename").GetString()).ToList();
        var page2Files = page2.GetProperty("completions").EnumerateArray()
            .Select(i => i.GetProperty("filename").GetString()).ToList();

        Assert.Equal(2, page2Files.Count);
        Assert.Empty(page1Files.Intersect(page2Files));
        Assert.Equal(new[] { "cot_03.edi", "cot_04.edi" }, page2Files);
    }

    [Fact]
    public async Task Get_Completions_SinceInvalido_Retorna400()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var resp = await Get(client, "/completions?since=no-es-fecha");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Completions_LimitInvalido_Retorna400()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var resp = await Get(client, "/completions?limit=abc");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Completions_MarcadorConNombreInvalido_SeIgnora()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        Directory.CreateDirectory(factory.ResultsFolder);
        // No matchea cot_<REF>.<digitos> — se ignoran sin error.
        await File.WriteAllTextAsync(Path.Combine(factory.ResultsFolder, "ruido.txt"), "");
        await File.WriteAllTextAsync(Path.Combine(factory.ResultsFolder, "cot_X.noesnumero"), "");

        // Y uno válido para contraste.
        SeedMarker(factory, "cot_OK.7", T1);

        var resp = await Get(client, "/completions");
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        var items = root.GetProperty("completions");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("cot_OK.edi", items[0].GetProperty("filename").GetString());
    }

    // ─── /results/{name}/archive ───

    [Fact]
    public async Task Post_ResultsArchive_MueveMarcadorAArchive()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();
        SeedMarker(factory, "cot_OK.7", T1);

        var resp = await Post(client, "/results/cot_OK.7/archive");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.True(root.GetProperty("archived").GetBoolean());

        // Ya no está en Results\ (top-level) y hay una copia en archive\.
        Assert.False(File.Exists(Path.Combine(factory.ResultsFolder, "cot_OK.7")));
        var archived = Directory.GetFiles(Path.Combine(factory.ResultsFolder, "archive"), "cot_OK.7_*");
        Assert.Single(archived);
    }

    [Fact]
    public async Task Post_ResultsArchive_Inexistente_EsIdempotente()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var resp = await Post(client, "/results/cot_NOPE.1/archive");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.False(root.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Post_ResultsArchive_SinApiKey_Retorna401()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var resp = await client.PostAsync("/results/cot_OK.7/archive", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ─── Helpers ───

    private static Task<HttpResponseMessage> Get(HttpClient client, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-API-Key", ApiKey);
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-API-Key", ApiKey);
        return client.SendAsync(req);
    }

    // Crea un marcador vacío cot_<REF>.<docid> con mtime controlado (el cursor
    // de /completions es el mtime).
    private static void SeedMarker(TestDropFactory factory, string name, DateTime mtimeUtc)
    {
        Directory.CreateDirectory(factory.ResultsFolder);
        var path = Path.Combine(factory.ResultsFolder, name);
        File.WriteAllText(path, string.Empty);
        File.SetLastWriteTimeUtc(path, mtimeUtc);
    }
}
