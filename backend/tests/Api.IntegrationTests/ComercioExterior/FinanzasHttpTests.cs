using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ComercioExterior;
/// <summary>Requiere PostgreSQL aislado; se deja para la corrida Docker de integración.</summary>
public sealed class FinanzasHttpTests
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    private static async Task<JsonElement> Json(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static Task<HttpResponseMessage> Send(HttpClient client, string url, object body, string? etag, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        if (etag is not null) request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        return client.SendAsync(request);
    }
    [Fact]
    public async Task CA11_permisos_y_sucursal_en_finanzas_y_mutaciones()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var admin = await e.ClienteAsync(true);
        var creado = await admin.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid();
        using var sinPermiso = await e.ClienteAsync(true, soloLeer: true);
        using var ajeno = await e.ClienteAsync(true, asociar: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.GetAsync($"{Base}/{id}/finanzas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync($"{Base}/{id}/gastos/ordenes-servicio")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.GetAsync($"{Base}/{id}/gastos/ordenes-servicio")).StatusCode);
        foreach (var ruta in new[] { "gastos/conceptos/inicializar", "gastos/distribuir", "anticipos" })
        {
            var body = ruta == "anticipos" ? (object)new { agenteId = Guid.NewGuid(), pedimento = "TEST", impuestos = new { iva = 100, igi = 0, prv = 0 }, fecha = "2026-10-01" } : new { };
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(sinPermiso, $"{Base}/{id}/{ruta}", body, creado.Headers.ETag!.Tag)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(ajeno, $"{Base}/{id}/{ruta}", body, creado.Headers.ETag!.Tag)).StatusCode);
        }
    }
    [Fact]
    public async Task Catalogo_etag_idempotencia_y_cierre_bloqueado_por_anticipo()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var client = await e.ClienteAsync(true);
        var creado = await client.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await Json(creado)).GetProperty("expediente").GetProperty("id").GetGuid(); var etag = creado.Headers.ETag!.Tag;
        Assert.Equal((HttpStatusCode)428, (await Send(client, $"{Base}/{id}/gastos/conceptos/inicializar", new { }, null)).StatusCode);
        var key = Guid.NewGuid().ToString();
        var primera = await Send(client, $"{Base}/{id}/gastos/conceptos/inicializar", new { }, etag, key); primera.EnsureSuccessStatusCode();
        var repetida = await Send(client, $"{Base}/{id}/gastos/conceptos/inicializar", new { }, etag, key); repetida.EnsureSuccessStatusCode();
        Assert.Equal(await primera.Content.ReadAsStringAsync(), await repetida.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, $"{Base}/{id}/gastos/conceptos/inicializar", new { }, etag)).StatusCode);
        var financiero = await Json(primera); Assert.Equal(13, financiero.GetProperty("conceptos").GetArrayLength());
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var maestros = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
        var proveedor = await maestros.Proveedores.SingleAsync(p => p.Clave == "DEMO-PROV-ACT");
        var anticipo = await Send(client, $"{Base}/{id}/anticipos", new { agenteId = proveedor.Id, pedimento = "TEST-ANT", impuestos = new { iva = 150000, igi = 0, prv = 0 }, fecha = "2026-10-01" }, primera.Headers.ETag!.Tag);
        anticipo.EnsureSuccessStatusCode();
        var cierre = await Send(client, $"{Base}/{id}/cerrar", new { }, anticipo.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cierre.StatusCode);
        Assert.Contains("Anticipos sin comprobar", await cierre.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
        Assert.Single(await db.AnticiposAgente.Where(a => a.ExpedienteId == id).ToListAsync());
    }
}
