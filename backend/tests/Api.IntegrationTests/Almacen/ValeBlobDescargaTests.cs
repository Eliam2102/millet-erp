using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports.Blob;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.Stubs;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Descarga del vale escaneado de una salida (<c>GET /api/v1/almacen/salidas/{id}/vale</c>)
/// contra el adaptador real de blob del host de pruebas (<see cref="LocalFilesystemAlmacenBlobStub"/>
/// sobre un directorio temporal, sin mocks). Cubre el caso feliz (200) y el caso en que la
/// salida apunta a un blob que ya no existe, que debe responder 404 y no 500.
/// </summary>
public sealed class ValeBlobDescargaTests : IClassFixture<ValeBlobDescargaFactory>
{
    private const string SuperAdminOid = "dev-superadmin";

    // Mismo empresa sintético que usan los fixtures P6: el superadmin de pruebas pertenece a él.
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly ValeBlobDescargaFactory _factory;

    public ValeBlobDescargaTests(ValeBlobDescargaFactory factory) => _factory = factory;

    [Fact]
    public async Task Vale_con_blob_existente_responde_200()
    {
        var client = await LoginSuperAdminAsync();
        var (salidaId, _) = await SembrarValeAsync();
        try
        {
            using var respuesta = await client.GetAsync($"/api/v1/almacen/salidas/{salidaId}/vale");

            Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.Equal("%PDF-1.4 vale de prueba", await respuesta.Content.ReadAsStringAsync());
        }
        finally
        {
            await LimpiarAsync(salidaId);
        }
    }

    [Fact]
    public async Task Vale_con_blob_borrado_responde_404_y_no_500()
    {
        var client = await LoginSuperAdminAsync();
        var (salidaId, blobUrl) = await SembrarValeAsync();
        try
        {
            File.Delete(new Uri(blobUrl).LocalPath);

            using var respuesta = await client.GetAsync($"/api/v1/almacen/salidas/{salidaId}/vale");

            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        }
        finally
        {
            await LimpiarAsync(salidaId);
        }
    }

    /// <summary>Sube un vale real al stub y deja una salida por vale que lo referencia.</summary>
    private async Task<(Guid SalidaId, string BlobUrl)> SembrarValeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var blob = scope.ServiceProvider.GetRequiredService<IAlmacenarBlobPort>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var blobUrl = await blob.SubirAsync(
            Guid.NewGuid(),
            new MemoryStream("%PDF-1.4 vale de prueba"u8.ToArray()),
            "application/pdf",
            "vale.pdf",
            CancellationToken.None);

        var salida = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.SalidaPorVale, Empresa, new(2026, 10, 9));
        salida.VincularSalida(null, blobUrl, null);
        db.Movimientos.Add(salida);
        await db.SaveChangesAsync();
        return (salida.Id, blobUrl);
    }

    private async Task LimpiarAsync(Guid salidaId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        await db.Movimientos.Where(m => m.Id == salidaId).ExecuteDeleteAsync();
    }

    private async Task<HttpClient> LoginSuperAdminAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = SuperAdminOid,
            Email = "superadmin@dev.local",
            Nombre = "Super Admin Dev",
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>
/// Host de la suite de vales: fuerza el stub local de blob sobre un directorio temporal propio,
/// para no depender de Azure Blob Storage aunque la configuración del entorno lo tenga.
/// </summary>
public sealed class ValeBlobDescargaFactory : WebApplicationFactory<Program>
{
    public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "millet-vale-test-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAlmacenarBlobPort>();
            services.AddSingleton<IAlmacenarBlobPort>(_ => new LocalFilesystemAlmacenBlobStub(
                Options.Create(new LocalFilesystemAlmacenBlobStubOptions { RootPath = RootPath }),
                NullLogger<LocalFilesystemAlmacenBlobStub>.Instance));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
