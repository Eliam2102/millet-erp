using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Domain.Cce;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using UglyToad.PdfPig;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// Documentos fiscales impresos, parte A (86e3tfdx0): descarga real del PDF del
/// complemento de pago (2 facturas) y de la factura de exportación con su bloque
/// de comercio exterior. Endpoint + PostgreSQL desechable; datos ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed partial class DocumentosFiscalesImpresosEndpointsTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesEmisor Emisor = new("EKU9003173C9", "Emisor ficticio 86e3tfdx0", "601", "97000");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio 86e3tfdx0", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesReceptor ReceptorPago = Receptor with { UsoCfdi = "CP01" };
    private static readonly DatosFiscalesReceptor ReceptorExtranjero =
        new("XEXX010101000", "Demo Glass Imports LLC", "616", "97000", "S01", "USA", false);
    private static readonly string Sello = string.Concat(Enumerable.Repeat("AAAA", 86));

    [Fact]
    public async Task El_complemento_de_pago_se_descarga_con_las_dos_facturas_parcialidades_saldos_y_timbre()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var a = Factura($"TFA-{sufijo}", 3000m);
        var b = Factura($"TFB-{sufijo}", 2000m);
        var uuidA = Guid.NewGuid().ToString();
        var uuidB = Guid.NewGuid().ToString();
        Timbrar(a, uuidA);
        Timbrar(b, uuidB);
        var rep = ReciboPago.CrearBorrador(Empresa, $"TFP-{sufijo}", 1, TestComprasFixtures.SucursalMid, null, null,
            ReceptorPago, Emisor, 2026, 10, DateTimeOffset.UtcNow, "MXN");
        rep.AgregarFacturaPagada(a.Id, uuidA, 1, "MXN", 1000m, a.Total, "03", null, null, null, null, "SPEI-DEMO-1", "01", a.Total, []);
        rep.AgregarFacturaPagada(b.Id, uuidB, 2, "MXN", 500m, 1500m, "03", null, null, null, null, null, "01", b.Total, []);
        rep.EstablecerImporteTotalPago(1500m);
        var uuidRep = Guid.NewGuid().ToString();
        Timbrar(rep, uuidRep);

        try
        {
            db.AddRange(a, b, rep);
            await db.SaveChangesAsync();

            var response = await client.GetAsync($"/api/v1/facturacion/repp/{rep.Id}/pdf");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal($"complemento-pago-{rep.Folio}.pdf", response.Content.Headers.ContentDisposition?.FileName);
            using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
            var texto = SinEspacios(string.Concat(pdf.GetPages().Select(p => p.Text)));
            Assert.Contains("COMPLEMENTODEPAGO/PAYMENTRECEIPT", texto);
            Assert.Contains(uuidA.ToUpperInvariant(), texto);
            Assert.Contains(uuidB.ToUpperInvariant(), texto);
            Assert.Contains(a.Folio, texto);
            Assert.Contains(b.Folio, texto);
            Assert.Contains("1,500.00MXN", texto);
            Assert.Contains("SPEI-DEMO-1", texto);
            Assert.Contains("SelloDigitaldelSAT", texto);
            Assert.NotEmpty(pdf.GetPages().SelectMany(p => p.GetImages()));
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.Remove(await db.RecibosPago.Include(r => r.FacturasPagadas).SingleAsync(r => r.Id == rep.Id));
            db.RemoveRange(await db.FacturasVenta.Where(f => f.Id == a.Id || f.Id == b.Id).ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task La_factura_de_exportacion_se_descarga_con_el_bloque_de_comercio_exterior()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var f = FacturaVenta.CrearBorrador(Empresa, $"TFE-{Guid.NewGuid():N}"[..12], 1, TestComprasFixtures.SucursalMid,
            null, null, ReceptorExtranjero, Emisor, "PPD", "99", "USD", 18.5432m, 2026, 10, 1,
            ComportamientoFiscal.ExportacionConCce, null, null, null, false);
        f.AgregarLinea(null, "30171710", "INSULATED GLASS DEMO", "H87", 2, 750.125m, 0, "02", 0m, null, null);
        f.RecalcularTotales();
        var cce = ComplementoCce.Crear(f.Id, "2", "CIF", 18.5432m, "DEMO-TAX-456", "USA", claveDePedimento: "A1");
        cce.AgregarLinea("7008000101", "06", 2, 750.125m, 1500.25m, true);
        f.AdjuntarCce(cce);
        Timbrar(f, Guid.NewGuid().ToString());

        try
        {
            db.Add(f);
            await db.SaveChangesAsync();

            var response = await client.GetAsync($"/api/v1/facturacion/facturas/{f.Id}/pdf");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
            var texto = SinEspacios(string.Concat(pdf.GetPages().Select(p => p.Text)));
            Assert.Contains("COMERCIOEXTERIOR/FOREIGNTRADE", texto);
            Assert.Contains("Incoterm:CIF", texto);
            Assert.Contains("18.543200", texto);
            Assert.Contains("1,500.25USD", texto);
            Assert.Contains("DEMO-TAX-456", texto);
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.Remove(await db.FacturasVenta.Include(x => x.ComplementoCce!).ThenInclude(c => c.Lineas).SingleAsync(x => x.Id == f.Id));
            await db.SaveChangesAsync();
        }
    }

    private static FacturaVenta Factura(string folio, decimal importe)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, folio, 1, TestComprasFixtures.SucursalMid, null, null, Receptor, Emisor,
            "PPD", "99", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "30171700", "VIDRIO DEMO", "H87", 1, importe, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        return f;
    }

    private static void Timbrar(Comprobante c, string uuid)
    {
        c.MarcarTimbradoEnProceso();
        c.MarcarTimbrado(uuid, Sello, Sello, "30001000000500003456", DateTimeOffset.UtcNow, "SPR190613I52", null);
    }

    private async Task<HttpClient> Login()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local", Nombre = "Prueba 86e3tfdx0", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }

    private static string SinEspacios(string texto) => Espacios().Replace(texto, "");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();
}
