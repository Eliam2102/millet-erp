using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Millet.AwDropService.Tests;

// ============================================================================
// DocumentsEndpointTests
// Cubren GET /documents y GET /documents/{filename}:
//   - Auth (X-API-Key faltante / incorrecta → 401)
//   - Carpeta vacía → documents [] + next_since null
//   - Parseo del nombre: doc_type + aw_doc_id desde oferta_/pedido_<nro>.pdf
//   - Orden por ModifiedAt ASC + cursor since estricto (>) + limit clamp
//   - since / limit inválidos → 400
//   - Nombres que no matchean la convención se ignoran
//   - Descarga: happy path 200 application/pdf; nombre inválido / inexistente
//     / traversal → 404
// ============================================================================

public class DocumentsEndpointTests
{
    private const string ApiKey = "test-api-key-32-bytes-aaaaaaaaaa";

    [Fact]
    public async Task Get_Documents_SinApiKey_Retorna401()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        var resp = await client.GetAsync("/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Documents_ConApiKeyIncorrecta_Retorna401()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/documents");
        req.Headers.Add("X-API-Key", "wrong-key-same-length-aaaaaaaaaa");

        var resp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Documents_CarpetaVacia_RetornaArrayVacioYNextSinceNull()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        var resp = await Get(client, "/documents");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, root.GetProperty("documents").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("next_since").ValueKind);
    }

    [Fact]
    public async Task Get_Documents_OfertaYPedido_ParseaDocTypeYAwDocId()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        SeedPdf(factory, "oferta_4614.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));
        SeedPdf(factory, "pedido_4615.pdf", new DateTime(2026, 6, 30, 11, 0, 0, DateTimeKind.Utc));

        var resp = await Get(client, "/documents");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var items = root.GetProperty("documents");
        Assert.Equal(2, items.GetArrayLength());

        // Orden ASC por ModifiedAt: oferta (10:00) antes que pedido (11:00).
        var oferta = items[0];
        Assert.Equal("oferta_4614.pdf", oferta.GetProperty("filename").GetString());
        Assert.Equal("oferta", oferta.GetProperty("doc_type").GetString());
        Assert.Equal(4614L, oferta.GetProperty("aw_doc_id").GetInt64());
        Assert.True(oferta.GetProperty("size_bytes").GetInt64() > 0);

        var pedido = items[1];
        Assert.Equal("pedido_4615.pdf", pedido.GetProperty("filename").GetString());
        Assert.Equal("pedido", pedido.GetProperty("doc_type").GetString());
        Assert.Equal(4615L, pedido.GetProperty("aw_doc_id").GetInt64());

        // next_since == modified_at del último.
        Assert.Equal(pedido.GetProperty("modified_at").GetString(),
            root.GetProperty("next_since").GetString());
    }

    [Fact]
    public async Task Get_Documents_ConSince_DevuelveSoloPosteriores()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        SeedPdf(factory, "oferta_1.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));
        SeedPdf(factory, "oferta_2.pdf", new DateTime(2026, 6, 30, 11, 0, 0, DateTimeKind.Utc));
        SeedPdf(factory, "oferta_3.pdf", new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        // since == modified_at del primero → devuelve 2 y 3 (estrictamente posterior).
        var resp = await Get(client, "/documents?since=2026-06-30T10:00:00.000Z");

        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        var items = root.GetProperty("documents");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("oferta_2.pdf", items[0].GetProperty("filename").GetString());
        Assert.Equal("oferta_3.pdf", items[1].GetProperty("filename").GetString());
    }

    [Fact]
    public async Task Get_Documents_ConLimit_RespetaLimitYNextSinceApuntaAlUltimo()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        for (int i = 1; i <= 5; i++)
        {
            SeedPdf(factory, $"pedido_{i}.pdf", new DateTime(2026, 6, 30, 10, i, 0, DateTimeKind.Utc));
        }

        var resp = await Get(client, "/documents?limit=2");
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        var items = root.GetProperty("documents");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("pedido_1.pdf", items[0].GetProperty("filename").GetString());
        Assert.Equal("pedido_2.pdf", items[1].GetProperty("filename").GetString());
        Assert.Equal(items[1].GetProperty("modified_at").GetString(),
            root.GetProperty("next_since").GetString());
    }

    [Fact]
    public async Task Get_Documents_SinceInvalido_Retorna400()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        var resp = await Get(client, "/documents?since=no-es-fecha");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Documents_LimitInvalido_Retorna400()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        var resp = await Get(client, "/documents?limit=abc");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Documents_NombresInvalidos_SeIgnoran()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        // No matchean la convención → ignorados sin error.
        SeedPdf(factory, "factura_99.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));
        SeedPdf(factory, "oferta_.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));
        SeedPdf(factory, "oferta_4614.txt", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));
        // Uno válido para contraste.
        SeedPdf(factory, "oferta_4614.pdf", new DateTime(2026, 6, 30, 11, 0, 0, DateTimeKind.Utc));

        var resp = await Get(client, "/documents");
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        var items = root.GetProperty("documents");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("oferta_4614.pdf", items[0].GetProperty("filename").GetString());
    }

    [Fact]
    public async Task Get_Documents_Download_HappyPath_RetornaPdf()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        SeedPdf(factory, "oferta_4614.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc),
            content: "%PDF-1.4 fake content");

        var resp = await Get(client, "/documents/oferta_4614.pdf");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Get_Documents_Download_SinApiKey_Retorna401()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        SeedPdf(factory, "oferta_4614.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));

        var resp = await client.GetAsync("/documents/oferta_4614.pdf");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Documents_Download_Inexistente_Retorna404()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        var resp = await Get(client, "/documents/oferta_9999.pdf");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Theory]
    [InlineData("/documents/factura_1.pdf")]   // prefijo inválido
    [InlineData("/documents/oferta_1.txt")]    // extensión inválida
    public async Task Get_Documents_Download_NombreInvalido_Retorna404(string url)
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        var resp = await Get(client, url);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Archive_HappyPath_MueveYDesapareceDeLaLista()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        SeedPdf(factory, "oferta_4614.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));

        var resp = await Post(client, "/documents/oferta_4614.pdf/archive");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.True(root.GetProperty("archived").GetBoolean());

        // Ya no está en la carpeta caliente ni lo lista /documents.
        Assert.False(File.Exists(Path.Combine(factory.DocumentsFolder, "oferta_4614.pdf")));
        var list = JsonDocument.Parse(await (await Get(client, "/documents")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, list.GetProperty("documents").GetArrayLength());

        // Quedó en archive\ con el nombre renombrado (_<stamp>).
        var archiveDir = Path.Combine(factory.DocumentsFolder, "archive");
        var archived = Directory.GetFiles(archiveDir, "oferta_4614_*.pdf");
        Assert.Single(archived);
    }

    [Fact]
    public async Task Post_Archive_Inexistente_RetornaArchivedFalse()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();

        var resp = await Post(client, "/documents/oferta_9999.pdf/archive");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        Assert.False(root.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Post_Archive_SinApiKey_Retorna401()
    {
        using var factory = new TestDocumentsFactory();
        var client = factory.CreateClient();
        SeedPdf(factory, "oferta_4614.pdf", new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc));

        var resp = await client.PostAsync("/documents/oferta_4614.pdf/archive", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        // No se movió.
        Assert.True(File.Exists(Path.Combine(factory.DocumentsFolder, "oferta_4614.pdf")));
    }

    // ─── Helpers ───

    private static Task<HttpResponseMessage> Post(HttpClient client, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("X-API-Key", ApiKey);
        return client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-API-Key", ApiKey);
        return client.SendAsync(req);
    }

    private static void SeedPdf(
        TestDocumentsFactory factory,
        string filename,
        DateTime modifiedUtc,
        string content = "%PDF-1.4")
    {
        var path = Path.Combine(factory.DocumentsFolder, filename);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, modifiedUtc);
    }
}

// ============================================================================
// TestDocumentsFactory
// WebApplicationFactory que configura una carpeta de documentos única por
// test (DropService:Documents:Folders:0) y la limpia al Dispose.
// ============================================================================

internal sealed class TestDocumentsFactory : WebApplicationFactory<Program>
{
    public string ImportFolder { get; }
    public string DocumentsFolder { get; }

    public TestDocumentsFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), "docs-test-" + Guid.NewGuid().ToString("N"));
        ImportFolder = Path.Combine(root, "Import");
        DocumentsFolder = Path.Combine(root, "Documents");
        Directory.CreateDirectory(ImportFolder);
        Directory.CreateDirectory(DocumentsFolder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DropService:ApiKey"]            = "test-api-key-32-bytes-aaaaaaaaaa",
                ["DropService:AwImportFolder"]    = ImportFolder,
                ["DropService:Documents:Folders:0"] = DocumentsFolder,
                ["Logging:EventLog:LogLevel:Default"] = "None",
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            var root = Directory.GetParent(ImportFolder)?.FullName;
            if (root is not null && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
        base.Dispose(disposing);
    }
}
