using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using CartaPorteCfdi = Millet.Facturacion.Domain.CartaPorte.CartaPorte;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// 86e3tfdx0 (criterio 4): GET real del PDF de la Carta Porte + handler + PostgreSQL desechable.
/// Antes respondía 422 PDF_NO_SOPORTADO. Todos los datos son ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CartaPortePdfEndpointsTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Destinatario ficticio CP-PDF", "601", "97000", "S01", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio CP-PDF", "601", "97000");

    [Fact]
    public async Task Descarga_el_pdf_de_una_carta_porte_timbrada_con_el_formato_de_Millet()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var vehiculo = Vehiculo.Crear(Empresa, $"FX{sufijo}", "C2", 2022, "TPAF01", "SCT-FIX", "SEGUROS FIX", "POL-FIX", 17.5m);
        var operador = Operador.Crear(Empresa, $"XAXX{sufijo}", "OPERADOR FICTICIO", "LIC-FIX");
        var cp = CartaPorteCfdi.CrearBorrador(Empresa, TipoComprobante.Traslado, $"FIXCP-{sufijo}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "MXN", 2026, 10, "PLANTA FICTICIA", "BODEGA FICTICIA", 120m,
            vehiculo.Id, operador.Id, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(4),
            origenCodigoPostal: "97000", origenEstado: "YUC", destinoCodigoPostal: "77500", destinoEstado: "ROO");
        cp.AgregarMercancia("VIDRIO FICTICIO", "30171700", "H87", 4, 120m, false);
        cp.MarcarTimbradoEnProceso();
        cp.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        try
        {
            db.AddRange(vehiculo, operador, cp);
            await db.SaveChangesAsync();

            var response = await client.GetAsync($"/api/v1/facturacion/carta-porte/{cp.Id}/pdf");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal($"carta-porte-FIXCP-{sufijo}.pdf", response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.StartsWith("%PDF", Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync())[..4]);

            // La familia se respeta: el id de una Carta Porte no se descarga por la ruta de facturas.
            var otraFamilia = await client.GetAsync($"/api/v1/facturacion/facturas/{cp.Id}/pdf");
            Assert.Equal(HttpStatusCode.NotFound, otraFamilia.StatusCode);
        }
        finally
        {
            db.Remove(cp);
            db.RemoveRange(vehiculo, operador);
            await db.SaveChangesAsync();
        }
    }

    private async Task<HttpClient> Login()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }
}
