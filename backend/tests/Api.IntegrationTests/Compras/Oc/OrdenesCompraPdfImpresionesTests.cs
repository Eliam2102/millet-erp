using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Application.Oc.Eventos;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

public partial class OrdenesCompraEndpointsTests
{
    [Fact]
    public async Task Pdf_dos_descargas_y_vista_previa_cuentan_solo_descargas()
    {
        var client = await CreateSuperAdminClientAsync();
        var ocId = await CrearOcAsync(client);
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            // Preparación aislada del endpoint PDF; autorización se prueba en su propia suite.
            db.Entry(oc).Property(o => o.Estado).CurrentValue = EstadoOrdenCompra.Autorizada;
            var blob = sp.GetRequiredService<IAlmacenarBlobPort>();
            using var contenido = new MemoryStream("%PDF-1.4 prueba ficticia"u8.ToArray());
            var url = await blob.SubirAsync(Guid.NewGuid(), contenido, "application/pdf", "prueba.pdf", CancellationToken.None);
            db.OrdenCompraPdfs.Add(new OrdenCompraPdf(Guid.NewGuid(), ocId, url, "application/pdf", contenido.Length,
                DateTimeOffset.UtcNow, "Prueba ficticia"));
            await db.SaveChangesAsync();
        }

        var preview = await client.GetAsync($"{EndpointBase}/{ocId}/pdf?vistaPrevia=true");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.False(preview.Headers.Contains("X-Oc-Impresion"));
        var primera = await client.GetAsync($"{EndpointBase}/{ocId}/pdf");
        primera.EnsureSuccessStatusCode();
        Assert.Equal("1", primera.Headers.GetValues("X-Oc-Impresion").Single());
        var otraPreview = await client.GetAsync($"{EndpointBase}/{ocId}/pdf?vistaPrevia=true");
        otraPreview.EnsureSuccessStatusCode();
        var segunda = await client.GetAsync($"{EndpointBase}/{ocId}/pdf");
        segunda.EnsureSuccessStatusCode();
        Assert.Equal("2", segunda.Headers.GetValues("X-Oc-Impresion").Single());
        Assert.Equal(await primera.Content.ReadAsByteArrayAsync(), await segunda.Content.ReadAsByteArrayAsync());
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        Assert.Equal(2, detalle.GetProperty("impresiones").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.String, detalle.GetProperty("ultimaImpresionEn").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.String, detalle.GetProperty("ultimaImpresionPor").ValueKind);

        // El mismo listener que se ejecuta al reautorizar reemplaza la fila.
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            await sp.GetRequiredService<MediatR.IPublisher>().Publish(
                new OrdenCompraAutorizadaPersistida(oc.Id, oc.Folio.Valor));
        }
        var nueva = await client.GetAsync($"{EndpointBase}/{ocId}/pdf");
        nueva.EnsureSuccessStatusCode();
        Assert.Equal("1", nueva.Headers.GetValues("X-Oc-Impresion").Single());
    }
}
