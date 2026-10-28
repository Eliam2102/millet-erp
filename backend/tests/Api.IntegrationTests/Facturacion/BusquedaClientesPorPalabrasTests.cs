using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// 86e3tfdx8 — búsqueda parcial de clientes por palabras contra PostgreSQL real
/// (translate() no corre en memoria): todas las palabras, en cualquier orden y
/// sin acentos, en los buscadores de Facturación, CxC y caja. Datos ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class BusquedaClientesPorPalabrasTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio 86e3tfdx8", "601", "97000");

    [Fact]
    public async Task Facturacion_y_CxC_encuentran_por_palabras_en_cualquier_orden_y_sin_acentos()
    {
        var s = Sufijo();
        var invertido = await SembrarClienteAsync($"PÉREZ GARCÍA CARLOS {s}");
        var conSa = await SembrarClienteAsync($"Carlos Pérez {s} SA de CV");
        var otro = await SembrarClienteAsync($"Juan López {s}");
        using var client = await Login();

        foreach (var url in new[]
                 {
                     $"/api/v1/facturacion/catalogos/clientes?q={Uri.EscapeDataString($"carlos perez {s}")}",
                     $"/api/v1/cuentas-por-cobrar/clientes-lookup?q={Uri.EscapeDataString($"Carlos Pérez {s}")}",
                 })
        {
            var ids = await Ids(client, url);
            Assert.Contains(invertido, ids);
            Assert.Contains(conSa, ids);
            Assert.DoesNotContain(otro, ids);
        }

        Assert.Empty(await Ids(client, $"/api/v1/facturacion/catalogos/clientes?q=zzz-no-existe-{s}"));
    }

    [Fact]
    public async Task Con_texto_devuelve_hasta_50_y_primero_los_que_empiezan_con_la_primera_palabra()
    {
        var s = Sufijo();
        for (var i = 0; i < 24; i++) await SembrarClienteAsync($"Vidrios {i:00} Norte {s}");
        var primero = await SembrarClienteAsync($"Norte {s} Cristales");
        using var client = await Login();

        foreach (var url in new[]
                 {
                     $"/api/v1/facturacion/catalogos/clientes?q={Uri.EscapeDataString($"norte {s}")}",
                     $"/api/v1/cuentas-por-cobrar/clientes-lookup?q={Uri.EscapeDataString($"norte {s}")}",
                 })
        {
            var ids = await Ids(client, url);
            Assert.Equal(25, ids.Count);
            Assert.Equal(primero, ids[0]);
        }
    }

    [Fact]
    public async Task Caja_encuentra_comprobantes_por_palabras_del_receptor_sin_acentos()
    {
        var s = Sufijo();
        using var client = await Login();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var factura = Factura($"PÉREZ GARCÍA CARLOS {s}");
        db.Add(factura);
        await db.SaveChangesAsync();
        try
        {
            var r = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/facturacion/cobros/cobrables?search={Uri.EscapeDataString($"carlos perez {s}")}");
            Assert.Contains(r.EnumerateArray(), i => i.GetProperty("comprobanteId").GetGuid() == factura.Id);

            var vacio = await client.GetFromJsonAsync<JsonElement>(
                $"/api/v1/facturacion/cobros/cobrables?search={Uri.EscapeDataString($"juan {s}")}");
            Assert.DoesNotContain(vacio.EnumerateArray(), i => i.GetProperty("comprobanteId").GetGuid() == factura.Id);
        }
        finally
        {
            db.Remove(factura);
            await db.SaveChangesAsync();
        }
    }

    private static string Sufijo() => $"x{Guid.NewGuid():N}"[..9];

    private async Task<Guid> SembrarClienteAsync(string razonSocial)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var cliente = new Cliente(Guid.CreateVersion7(), $"T-{Guid.NewGuid():N}"[..20], razonSocial);
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return cliente.Id;
    }

    private static async Task<List<Guid>> Ids(HttpClient client, string url)
    {
        var json = await client.GetFromJsonAsync<JsonElement>(url);
        return json.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    private static FacturaVenta Factura(string receptorNombre)
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", receptorNombre, "601", "97000", "G03", "MEX", false);
        var f = FacturaVenta.CrearBorrador(Empresa, $"FIX-{Guid.NewGuid():N}", 1, TestComprasFixtures.SucursalMid,
            null, null, receptor, Emisor, "PUE", "01", "MXN", null,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "FIX prueba ficticia 86e3tfdx8", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        return f;
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
