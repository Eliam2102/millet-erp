using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Millet.AwDropService.Tests;

// ============================================================================
// DropEndpointTests
// Cubren los contratos del drop service:
//   - Auth (X-API-Key faltante / incorrecta → 401)
//   - Filename inválido (path traversal, separadores, regex) → 400
//   - Body vacío / muy pequeño → 400
//   - Body sin marca #END# → 422
//   - Happy path → 200 + archivo escrito atómicamente al folder configurado
//   - GET /healthz → 200 con uptime y writable bool
//
// Cada test usa una instancia fresca de TestDropFactory que apunta
// AwImportFolder a un temp dir único y lo limpia al Dispose.
// ============================================================================

public class DropEndpointTests
{
    private const string ApiKey = "test-api-key-32-bytes-aaaaaaaaaa";
    // EDI mínimo válido: ≥100 bytes + contiene #END#.
    private static readonly string ValidEdi =
        "FH cot Q-2026-0001\r\n" +
        new string('X', 120) +
        "\r\n#END#\r\n";

    private static HttpRequestMessage BuildPost(
        string filename,
        string? body = null,
        string? apiKey = ApiKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/drop-edi");
        if (apiKey is not null)
        {
            req.Headers.Add("X-API-Key", apiKey);
        }
        req.Headers.Add("X-Filename", filename);
        var payload = body ?? ValidEdi;
        req.Content = new StringContent(payload, Encoding.UTF8, "text/plain");
        return req;
    }

    [Fact]
    public async Task Post_DropEdi_Without_ApiKey_Should_Return_401()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var req = BuildPost("cot_Q-2026-0001.edi", apiKey: null);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Post_DropEdi_With_Wrong_ApiKey_Should_Return_401()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var req = BuildPost("cot_Q-2026-0001.edi", apiKey: "wrong-key-same-length-aaaaaaaaaa");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Theory]
    [InlineData("../etc/passwd.edi")]
    [InlineData("..\\..\\windows\\system32.edi")]
    [InlineData("sub/folder/cot.edi")]
    [InlineData("sub\\folder\\cot.edi")]
    [InlineData("cot_Q-2026-0001.txt")] // wrong extension
    [InlineData("cot Q 2026.edi")]      // spaces not allowed
    [InlineData("")]                    // empty
    public async Task Post_DropEdi_With_Invalid_Filename_Should_Return_400(string filename)
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var req = BuildPost(filename);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_DropEdi_With_Empty_Body_Should_Return_400()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/drop-edi");
        req.Headers.Add("X-API-Key", ApiKey);
        req.Headers.Add("X-Filename", "cot_Q-2026-0002.edi");
        req.Content = new ByteArrayContent(Array.Empty<byte>());
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_DropEdi_With_Body_Too_Small_Should_Return_400()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        // < 100 bytes → cae en validación de tamaño mínimo, no en #END#.
        var req = BuildPost("cot_Q-2026-0003.edi", body: "tiny #END#");
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Post_DropEdi_Without_EndMarker_Should_Return_422()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        // ≥100 bytes pero sin #END#.
        var bodyWithoutMarker = "FH dummy\r\n" + new string('X', 200) + "\r\nNO_MARKER\r\n";
        var req = BuildPost("cot_Q-2026-0004.edi", body: bodyWithoutMarker);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Post_DropEdi_Without_MarkerFromAw_Should_Return_200_With_Stuck_Outcome()
    {
        // Sin que A+W escriba el marcador en Results\, el watcher debe esperar
        // el timeout (configurado a 2s en el test factory) y devolver
        // outcome=stuck. El archivo EDI igual queda escrito en WorkDir.
        using var factory = new TestDropFactory(waitTimeoutSeconds: 2);
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        const string filename = "cot_Q-2026-0005.edi";
        var req = BuildPost(filename);
        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(filename, root.GetProperty("filename").GetString());
        Assert.True(root.GetProperty("bytes_written").GetInt32() > 100);
        Assert.Equal("stuck", root.GetProperty("outcome").GetString());
        Assert.Equal("default", root.GetProperty("lane").GetString());
        Assert.True(root.GetProperty("waited_ms").GetInt32() >= 1500,
            "waited_ms debería estar cerca del timeout (2000ms) cuando A+W no responde.");

        var writtenPath = root.GetProperty("path").GetString();
        Assert.NotNull(writtenPath);
        Assert.True(File.Exists(writtenPath), $"Archivo no encontrado en {writtenPath}");

        Assert.StartsWith(
            Path.GetFullPath(factory.ImportFolder),
            Path.GetFullPath(writtenPath!),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(writtenPath + ".tmp"), "Quedó un .tmp huérfano");

        var contentOnDisk = await File.ReadAllTextAsync(writtenPath!);
        Assert.Contains("#END#", contentOnDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_Healthz_Should_Return_200_With_Uptime_And_Writable()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("alive", root.GetProperty("status").GetString());
        Assert.True(root.TryGetProperty("uptime_seconds", out var uptime) && uptime.GetInt64() >= 0);
        Assert.True(root.GetProperty("import_folder_writable").GetBoolean(),
            "import_folder_writable=false — el temp dir del test debería ser escribible.");
        Assert.False(string.IsNullOrEmpty(root.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Get_Root_Should_Return_200_With_Version_Text()
    {
        using var factory = new TestDropFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        Assert.StartsWith("Millet A+W Drop Service - v", body, StringComparison.Ordinal);
    }
}

// ============================================================================
// TestDropFactory
// WebApplicationFactory que sobreescribe la configuración para apuntar
// AwImportFolder a un temp dir único por test, deshabilita EventLog y
// fija una ApiKey predecible.
// ============================================================================

internal sealed class TestDropFactory : WebApplicationFactory<Program>
{
    public string ImportFolder { get; }
    public string ResultsFolder => Path.Combine(ImportFolder, "Results");
    private readonly int _waitTimeoutSeconds;

    public TestDropFactory(int waitTimeoutSeconds = 2)
    {
        ImportFolder = Path.Combine(Path.GetTempPath(), "drop-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ImportFolder);
        _waitTimeoutSeconds = waitTimeoutSeconds;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DropService:ApiKey"]          = "test-api-key-32-bytes-aaaaaaaaaa",
                ["DropService:AwImportFolder"]  = ImportFolder,
                ["DropService:MaxBodySizeMB"]   = "10",
                // Timeout corto para que tests "stuck" no esperen 120s.
                ["DropService:WaitTimeoutSeconds"] = _waitTimeoutSeconds.ToString(),
                ["DropService:PollIntervalMs"]     = "100",
                // Deshabilitar EventLog en tests para que no requiera permisos.
                ["Logging:EventLog:LogLevel:Default"] = "None",
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (Directory.Exists(ImportFolder))
            {
                Directory.Delete(ImportFolder, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; otro test puede tener un handle abierto en CI.
        }
        base.Dispose(disposing);
    }
}
