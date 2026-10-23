using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// F1-COM-10 G1.8: envío de la OC al proveedor (correo simulado / registro manual).
// Parcial de OrdenesCompraEndpointsTests para reusar SembrarP2 y los clientes autenticados.
public partial class OrdenesCompraEndpointsTests
{
    private const string CorreoDemoProveedor = "proveedor-demo@example.test";

    private async Task<(Guid OcId, HttpClient Client)> SembrarOcEnvioAsync(bool autorizada = true)
    {
        var client = await CreateSuperAdminClientAsync();
        var (ocId, _) = await SembrarP2(client, recepcion: false, autorizar: autorizada);
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var blobId = Guid.NewGuid();
        using var contenido = new MemoryStream(PdfMinimo);
        var url = await scope.ServiceProvider.GetRequiredService<IAlmacenarBlobPort>()
            .SubirAsync(blobId, contenido, "application/pdf", "DEMO-envio.pdf", default);
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        db.OrdenCompraPdfs.Add(new OrdenCompraPdf(blobId, ocId, url, "application/pdf",
            PdfMinimo.Length, DateTimeOffset.UtcNow, "DEMO"));
        await db.SaveChangesAsync();
        return (ocId, client);
    }

    private static Task<HttpResponseMessage> PostEnvioAsync(HttpClient client, Guid ocId, string? key, string modo = "correo")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{EndpointBase}/{ocId}/envios")
        { Content = JsonContent.Create(new { Destinatario = CorreoDemoProveedor, Modo = modo }) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task EnviosOc_Post_Y_Get_RegistranElEnvioSimulado()
    {
        var (ocId, client) = await SembrarOcEnvioAsync();

        var post = await PostEnvioAsync(client, ocId, Guid.NewGuid().ToString("D"));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal("simulado", (await ReadJsonAsync(post)).GetProperty("resultado").GetString());
        var historial = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}/envios"));
        Assert.True(historial.GetProperty("esSimulado").GetBoolean());
        Assert.True(historial.GetProperty("puedeEnviar").GetBoolean());
        var envio = historial.GetProperty("envios").EnumerateArray().Single();
        Assert.Equal(CorreoDemoProveedor, envio.GetProperty("destinatario").GetString());
        Assert.Equal("correo", envio.GetProperty("modo").GetString());
        Assert.Equal("simulado", envio.GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task EnviosOc_Post_SoloLectura_Retorna_403_Y_NoRegistra()
    {
        var (ocId, admin) = await SembrarOcEnvioAsync();
        var lector = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesLeer);

        var post = await PostEnvioAsync(lector, ocId, Guid.NewGuid().ToString("D"));

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        var historial = await ReadJsonAsync(await admin.GetAsync($"{EndpointBase}/{ocId}/envios"));
        Assert.Equal(0, historial.GetProperty("envios").GetArrayLength());
    }

    [Fact]
    public async Task EnviosOc_Post_SinIdempotencyKey_Retorna_400()
    {
        var (ocId, _) = await SembrarOcEnvioAsync();
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var post = await PostEnvioAsync(client, ocId, key: null);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        var historial = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}/envios"));
        Assert.Equal(0, historial.GetProperty("envios").GetArrayLength());
    }

    [Fact]
    public async Task EnviosOc_Post_MismaIdempotencyKey_RegistraUnSoloEnvio()
    {
        var (ocId, client) = await SembrarOcEnvioAsync();
        var key = Guid.NewGuid().ToString("D");

        Assert.Equal(HttpStatusCode.OK, (await PostEnvioAsync(client, ocId, key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostEnvioAsync(client, ocId, key)).StatusCode);

        var historial = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}/envios"));
        Assert.Equal(1, historial.GetProperty("envios").GetArrayLength());
    }

    [Fact]
    public async Task EnviosOc_Post_OcSinAutorizar_Retorna_422_Y_NoRegistra()
    {
        var (ocId, client) = await SembrarOcEnvioAsync(autorizada: false);

        var post = await PostEnvioAsync(client, ocId, Guid.NewGuid().ToString("D"));

        await AssertCodigoP2(post, HttpStatusCode.UnprocessableEntity, "OC_ENVIO_NO_AUTORIZADA");
        var historial = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}/envios"));
        Assert.False(historial.GetProperty("puedeEnviar").GetBoolean());
        Assert.Equal(0, historial.GetProperty("envios").GetArrayLength());
    }
}
