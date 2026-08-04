using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Millet.AwDropService.Tests;

// ============================================================================
// PerEdiFlowTests
// Tests E2E del flujo per-EDI con MARCADOR (Idea 4): el drop service escribe
// el .edi, espera a que aparezca cot_<REF>.<docid> en Results\ y reporta
// success + aw_doc_id (del nombre) o stuck (timeout). Sin contenido, sin log.
// ============================================================================

public class PerEdiFlowTests
{
    private const string ApiKey = "test-api-key-32-bytes-aaaaaaaaaa";
    private static readonly string ValidEdi =
        "FH cot Q-2026-0001\r\n" +
        new string('X', 120) +
        "\r\n#END#\r\n";

    [Fact]
    public async Task Drop_MarcadorAparece_OutcomeSuccess_ConDocId()
    {
        using var factory = new TestDropFactory(waitTimeoutSeconds: 10);
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        const string filename = "cot_Q-2026-0010.edi";

        // Simula A+W: tras 300ms, escribe el marcador vacío cot_Q-2026-0010.10427999
        var simulator = Task.Run(async () =>
        {
            await Task.Delay(300);
            Directory.CreateDirectory(factory.ResultsFolder);
            await File.WriteAllTextAsync(
                Path.Combine(factory.ResultsFolder, "cot_Q-2026-0010.10427999"), string.Empty);
        });

        var resp = await client.SendAsync(BuildPost(filename));
        await simulator;

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("success", root.GetProperty("outcome").GetString());
        Assert.Equal(10427999L, root.GetProperty("aw_doc_id").GetInt64());
        Assert.Equal("default", root.GetProperty("lane").GetString());
    }

    [Fact]
    public async Task Drop_SinMarcador_OutcomeStuck()
    {
        // A+W rechaza el EDI (no crea pedido) → no escribe marcador → el drop
        // cae en timeout=stuck. Indistinguible de "aún no procesado" (Idea 4).
        using var factory = new TestDropFactory(waitTimeoutSeconds: 2);
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var resp = await client.SendAsync(BuildPost("cot_Q-2026-0011.edi"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("stuck", root.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("aw_doc_id").ValueKind);
    }

    [Fact]
    public async Task Drop_MarcadorDeOtroEdi_NoMatchea_OutcomeStuck()
    {
        // Hay un marcador en Results\ pero de OTRO EDI. El watcher solo matchea
        // por el basename del EDI que dropeó → no lo confunde → stuck.
        using var factory = new TestDropFactory(waitTimeoutSeconds: 2);
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        Directory.CreateDirectory(factory.ResultsFolder);
        await File.WriteAllTextAsync(
            Path.Combine(factory.ResultsFolder, "cot_Q-2026-9999.55555"), string.Empty);

        var resp = await client.SendAsync(BuildPost("cot_Q-2026-0012.edi"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("stuck", root.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Drop_SerializaDrops_CuandoOtroEnVuelo()
    {
        // Dos drops concurrentes. El segundo bloquea en el semáforo hasta que
        // el primero libere. Ambos terminan stuck (sin marcador).
        using var factory = new TestDropFactory(waitTimeoutSeconds: 5);
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);

        var firstTask = client.SendAsync(BuildPost("cot_Q-A.edi"));
        await Task.Delay(200); // que entre primero al semáforo

        var secondTask = client.SendAsync(BuildPost("cot_Q-B.edi"));

        var first = await firstTask;
        var second = await secondTask;

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstWaited = JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("waited_ms").GetInt32();
        var secondWaited = JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("waited_ms").GetInt32();

        Assert.True(firstWaited >= 4500);
        Assert.True(secondWaited >= 4500);
    }

    private static HttpRequestMessage BuildPost(string filename)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/drop-edi");
        req.Headers.Add("X-API-Key", ApiKey);
        req.Headers.Add("X-Filename", filename);
        req.Content = new StringContent(ValidEdi, Encoding.UTF8, "text/plain");
        return req;
    }
}
