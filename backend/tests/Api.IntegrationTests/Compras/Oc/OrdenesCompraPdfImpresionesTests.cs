using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Oc.Autorizar;
using Millet.Compras.Application.Oc.Eventos;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using UglyToad.PdfPig;

namespace Millet.Api.IntegrationTests.Compras.Oc;

public partial class OrdenesCompraEndpointsTests
{
    [Fact]
    public async Task Pdf_dos_descargas_y_vista_previa_cuentan_solo_descargas()
    {
        var client = await CreateSuperAdminClientAsync();
        var ocId = await CrearOcAsync(client);
        string folio;
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            folio = oc.Folio.Valor;
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
        var textoPreview = await TextoPdfAsync(preview);
        Assert.DoesNotContain("ORIGINAL", textoPreview);
        Assert.DoesNotContain("COPIA", textoPreview);
        var primera = await client.GetAsync($"{EndpointBase}/{ocId}/pdf");
        primera.EnsureSuccessStatusCode();
        Assert.Equal("1", primera.Headers.GetValues("X-Oc-Impresion").Single());
        var otraPreview = await client.GetAsync($"{EndpointBase}/{ocId}/pdf?vistaPrevia=true");
        otraPreview.EnsureSuccessStatusCode();
        var segunda = await client.GetAsync($"{EndpointBase}/{ocId}/pdf");
        segunda.EnsureSuccessStatusCode();
        Assert.Equal("2", segunda.Headers.GetValues("X-Oc-Impresion").Single());
        // El PDF se dibuja en cada petición: la primera descarga es ORIGINAL y la segunda COPIA 1.
        Assert.Equal($"{folio}.pdf", primera.Content.Headers.ContentDisposition!.FileNameStar ?? primera.Content.Headers.ContentDisposition.FileName);
        Assert.Contains("ORIGINAL", await TextoPdfAsync(primera));
        var textoSegunda = await TextoPdfAsync(segunda);
        Assert.Contains("COPIA1·", textoSegunda);
        Assert.DoesNotContain("ORIGINAL", textoSegunda);
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
        Assert.Contains("ORIGINAL", await TextoPdfAsync(nueva));
    }

    [Fact]
    public async Task Pdf_oc_autorizada_trae_datos_reales_sin_ids()
    {
        var client = await CreateSuperAdminClientAsync();
        var (ocId, _) = await SembrarP2(client, recepcion: false, autorizar: false);
        string claveProveedor, rfc, claveArticulo;
        using (var scope = _factory.Services.CreateScope())
        {
            // La línea sembrada apunta a un artículo ficticio: se liga a uno del catálogo para ver su clave.
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var articulo = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Articulos.AsNoTracking().FirstAsync();
            claveArticulo = articulo.Clave;
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
            db.Entry(oc.Lineas.Single()).Property(l => l.ArticuloId).CurrentValue = articulo.Id;
            await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            // Otro firmante para N2; al publicarse la autorización el listener guarda el PDF.
            await new AutorizarOrdenCompraHandler(sp.GetRequiredService<ComprasDbContext>(),
                sp.GetRequiredService<CompartidoDbContext>(), new UsuarioP2(Guid.NewGuid()),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<IPublisher>())
                .Handle(new(ocId, Millet.Compras.Domain.NivelAutorizacion.Nivel2, null), default);
            var compartido = sp.GetRequiredService<CompartidoDbContext>();
            var proveedor = await compartido.Proveedores.AsNoTracking().SingleAsync(p => p.Id == ProveedorActivoId);
            (claveProveedor, rfc) = (proveedor.Clave, proveedor.Rfc);
        }

        var respuesta = await client.GetAsync($"{EndpointBase}/{ocId}/pdf?vistaPrevia=true");
        respuesta.EnsureSuccessStatusCode();
        var texto = await TextoPdfAsync(respuesta);

        Assert.Contains(claveProveedor.Replace(" ", ""), texto);
        Assert.Contains(rfc.Replace(" ", ""), texto);
        Assert.Contains(claveArticulo.Replace(" ", ""), texto);
        Assert.Contains("ORDENDECOMPRA", texto);
        Assert.Contains("Nota:\"Lasfacturasdeberánindicarelnúmerodeordendecompra\"", texto);
        Assert.DoesNotMatch("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-", texto);
        Assert.DoesNotContain("Gastosadicionales", texto);
    }

    // Texto del PDF sin espacios: QuestPDF coloca cada glifo y PdfPig no siempre recupera los espacios.
    private static async Task<string> TextoPdfAsync(HttpResponseMessage respuesta)
    {
        using var doc = PdfDocument.Open(await respuesta.Content.ReadAsByteArrayAsync());
        return Regex.Replace(string.Join(" ", doc.GetPages().Select(p => p.Text)), @"\s+", "");
    }
}
