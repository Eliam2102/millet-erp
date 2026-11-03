using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// MAESTROS-UX: los listados traen primero los completos y después los incompletos (misma definición que
/// el filtro) y luego la clave en orden natural (AW-1, AW-2, AW-10), sobre todo el
/// universo y no solo sobre la página.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class MaestrosOrdenListadoTests
{
    private readonly WebApplicationFactory<Program> _factory;

    public MaestrosOrdenListadoTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Clientes_completos_primero_incompletos_despues_y_clave_en_orden_natural_con_paginado()
    {
        var p = "ORD" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            foreach (var n in new[] { 10, 2, 1 })
                db.Clientes.Add(new Cliente(Guid.CreateVersion7(), $"{p}-{n}", $"CLIENTE {p}",
                    rfc: $"{p[3..]}{n:D3}XX1", regimenFiscal: "601", codigoPostalFiscal: "97000"));
            db.Clientes.Add(new Cliente(Guid.CreateVersion7(), $"{p}-900", $"CLIENTE {p}"));
            await db.SaveChangesAsync();
        }
        var c = await LoginAdminAsync();

        var todos = await Claves(c, $"/api/v1/datos-maestros/clientes?razonSocial=CLIENTE {p}");
        Assert.Equal(new[] { $"{p}-1", $"{p}-2", $"{p}-10", $"{p}-900" }, todos);

        // El orden aplica a todo el universo: la segunda página sigue la misma secuencia.
        var pag2 = await Claves(c, $"/api/v1/datos-maestros/clientes?razonSocial=CLIENTE {p}&offset=2&limit=2");
        Assert.Equal(new[] { $"{p}-10", $"{p}-900" }, pag2);
    }

    [Theory]
    [InlineData("/api/v1/datos-maestros/proveedores?limit=5")]
    [InlineData("/api/v1/datos-maestros/productos-aw?limit=5")]
    [InlineData("/api/v1/datos-maestros/articulos?limit=5")]
    public async Task Listados_con_el_nuevo_orden_responden_200(string url)
    {
        var c = await LoginAdminAsync();
        var r = await c.GetAsync(url);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
    }

    private static async Task<string[]> Claves(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        r.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("clave").GetString()!).ToArray();
    }

    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "dev-superadmin", EmpresaId = (Guid?)null,
        });
        resp.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
}
