using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Infrastructure.Correo;
using Millet.SharedKernel.Infrastructure.Persistence;

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

    /// <summary>
    /// 86e3txv0n: con transporte real (aquí en memoria) la OC sale con su PDF, la pantalla deja de
    /// decir «simulado» y en el servidor de pruebas el correo del proveedor se redirige a los buzones
    /// de prueba; el intento queda en core.correo_envio con el destinatario original.
    /// </summary>
    [Fact]
    public async Task EnviosOc_Post_ConTransporteReal_EnModoPruebas_RedirigeYRegistra()
    {
        var (ocId, _) = await SembrarOcEnvioAsync();
        var transporte = new CorreoSalienteEnMemoria();
        await using var fabrica = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICorreoSaliente>();
            services.AddSingleton<ICorreoSaliente>(sp => new ModoPruebasCorreoSaliente(
                new RegistroCorreoSaliente(transporte, "Memoria", sp.GetRequiredService<IServiceScopeFactory>(),
                    sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<RegistroCorreoSaliente>>()),
                new ModoPruebasCorreoOptions
                {
                    Activo = true, DominiosPermitidos = ["millet.example"], BuzonesPrueba = ["qa@example.test"],
                }));
        }));
        var client = fabrica.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev"));

        var post = await PostEnvioAsync(client, ocId, Guid.NewGuid().ToString("D"));

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal("aceptado", (await ReadJsonAsync(post)).GetProperty("resultado").GetString());
        var historial = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}/envios"));
        Assert.False(historial.GetProperty("esSimulado").GetBoolean());

        var correo = Assert.Single(transporte.Enviados);
        Assert.Equal(["qa@example.test"], correo.Para);
        Assert.StartsWith("[PRUEBAS] Orden de compra", correo.Asunto);
        Assert.Contains(CorreoDemoProveedor, correo.Html);
        Assert.Equal("application/pdf", Assert.Single(correo.AdjuntosOVacio).ContentType);

        using var scope = fabrica.Services.CreateScope();
        var fila = await scope.ServiceProvider.GetRequiredService<CoreDbContext>().CorreoEnvios.AsNoTracking()
            .SingleAsync(e => e.Modulo == "compras" && e.ReferenciaId == ocId);
        Assert.Equal("aceptado", fila.Estado);
        Assert.Equal(CorreoDemoProveedor, fila.ParaOriginal);
        Assert.Equal("qa@example.test", fila.Para);
    }
}
