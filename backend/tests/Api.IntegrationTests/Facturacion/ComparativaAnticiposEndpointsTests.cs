using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// F6.2: GET real de la comparativa de anticipos vs aplicaciones + handler +
/// adapters reales (nombre de sucursal desde compartido.sucursales, Capa A) +
/// PostgreSQL desechable. Todos los datos son ficticios.
/// </summary>
public sealed class ComparativaAnticiposEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Ruta = "/api/v1/facturacion/reportes/comparativa-anticipos";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio F6.2", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio F6.2", "601", "97000");

    [Fact]
    public async Task Comparativa_devuelve_forma_adr_0036_con_la_fila_del_anticipo()
    {
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();

        var clienteId = Guid.NewGuid();
        var anticipoId = Guid.CreateVersion7();
        var factura = FacturaAnticipo.CrearBorrador(Empresa, $"F62-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid,
            null, null, Receptor, Emisor, "03", "MXN", null, 2026, 10, TipoAnticipo.ClientesMxp, null,
            anticipoId, 100m, 0.16m, "Anticipo ficticio F6.2", 7);
        factura.MarcarTimbradoEnProceso();
        factura.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow.AddMinutes(-5), null, null);
        var anticipo = Anticipo.Crear(Empresa, clienteId, Receptor.Rfc, TipoAnticipo.ClientesMxp, "MXN",
            factura.Total, factura.Id, id: anticipoId);
        try
        {
            db.AddRange(factura, anticipo);
            await db.SaveChangesAsync();

            var reporte = await client.GetFromJsonAsync<JsonElement>($"{Ruta}?clienteId={clienteId}");

            Assert.Equal("Comparativa de anticipos vs aplicaciones", reporte.GetProperty("titulo").GetString());
            Assert.True(reporte.TryGetProperty("generadoEn", out _));
            Assert.Equal(clienteId.ToString(), reporte.GetProperty("filtrosAplicados").GetProperty("clienteId").GetString());
            Assert.Contains(reporte.GetProperty("columnas").EnumerateArray(),
                c => c.GetProperty("clave").GetString() == "egresoUuid");
            var fila = Assert.Single(reporte.GetProperty("filas").EnumerateArray());
            Assert.Equal("Anticipo", fila.GetProperty("movimiento").GetString());
            Assert.Equal(factura.Folio, fila.GetProperty("folioAnticipo").GetString());
            Assert.StartsWith("MID", fila.GetProperty("sucursal").GetString());
            Assert.Equal(116m, fila.GetProperty("cobrado").GetDecimal());
            Assert.Equal(116m, fila.GetProperty("saldo").GetDecimal());
            Assert.Equal("Pendiente", fila.GetProperty("estado").GetString());
            Assert.Equal(factura.Id, fila.GetProperty("facturaAnticipoId").GetGuid());
            Assert.Equal(116m, reporte.GetProperty("totales").GetProperty("saldo").GetDecimal());
        }
        finally
        {
            await db.Anticipos.Where(a => a.Id == anticipo.Id).ExecuteDeleteAsync();
            db.ChangeTracker.Clear();
            db.Remove(await db.FacturasAnticipo.SingleAsync(f => f.Id == factura.Id));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Rango_invertido_responde_400()
    {
        using var client = await Login();

        var response = await client.GetAsync($"{Ruta}?desde=2026-10-31T00:00:00Z&hasta=2026-10-01T00:00:00Z");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sin_permiso_de_reportes_responde_403()
    {
        using var client = await Login("test-no-perms");

        var response = await client.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba F6.2", EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return client;
    }
}
