using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>ALM-APP-2: POST /api/v1/integraciones/almacenes/mensajes (contrato v1).</summary>
[Collection(ApiCompartida.Nombre)]
public class AlmacenesAppMensajesTests
{
    private const string Ruta = "/api/v1/integraciones/almacenes/mensajes";
    private const string Llave = "llave-test-almacenes";
    private readonly WebApplicationFactory<Program> _factory;

    public AlmacenesAppMensajesTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private WebApplicationFactory<Program> ConLlave(string? llave) =>
        _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Integraciones:AlmacenesApp:ApiKey"] = llave ?? "" })));

    private static HttpRequestMessage Req(string id, string? headerId = null, string? llave = Llave, string tipo = "REQUISICION_CANCELADA")
    {
        var r = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(new
            {
                mensajeId = id, tipo, version = 1, fecha = "2026-10-06T17:47:00-06:00",
                origen = "AlmacenRQ_MP", referencia = "RQ-1", datos = new { },
            }),
        };
        r.Headers.Add("X-Mensaje-Id", headerId ?? id);
        if (llave is not null) r.Headers.Add("X-Millet-Almacenes-Key", llave);
        return r;
    }

    private static async Task<JsonElement> Acuse(HttpResponseMessage resp) =>
        (await resp.Content.ReadFromJsonAsync<JsonElement>());

    [Fact]
    public async Task Llave_incorrecta_o_ausente_da_404()
    {
        await using var f = ConLlave(Llave);
        var c = f.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req(Guid.NewGuid().ToString(), llave: "mala"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req(Guid.NewGuid().ToString(), llave: null))).StatusCode);
    }

    [Fact]
    public async Task Sin_llave_configurada_da_404()
    {
        await using var f = ConLlave(null);
        var resp = await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Mismo_mensaje_dos_veces_el_segundo_es_DUPLICADO_con_mismo_folio()
    {
        await using var f = ConLlave(Llave);
        var c = f.CreateClient();
        var id = Guid.NewGuid().ToString();
        var a1 = await Acuse(await c.SendAsync(Req(id)));
        var resp2 = await c.SendAsync(Req(id));
        var a2 = await Acuse(resp2);

        Assert.NotEqual(HttpStatusCode.Accepted, resp2.StatusCode);
        Assert.Equal(id, a2.GetProperty("mensajeId").GetString());
        Assert.Equal("DUPLICADO", a2.GetProperty("estado").GetString());
        Assert.Equal(a1.GetProperty("folio").ValueKind, a2.GetProperty("folio").ValueKind);
        Assert.Equal(a1.GetProperty("folio").ToString(), a2.GetProperty("folio").ToString());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.Equal(1, await db.MensajesApp.CountAsync(m => m.MensajeId == id));
    }

    [Fact]
    public async Task Header_distinto_del_sobre_da_ERROR_y_no_persiste()
    {
        await using var f = ConLlave(Llave);
        var id = Guid.NewGuid().ToString();
        var resp = await f.CreateClient().SendAsync(Req(id, headerId: "otro"));
        var a = await Acuse(resp);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.False(await db.MensajesApp.AnyAsync(m => m.MensajeId == id));
    }

    [Fact]
    public async Task Tipo_no_soportado_da_ERROR_y_persiste_la_fila()
    {
        await using var f = ConLlave(Llave);
        var id = Guid.NewGuid().ToString();
        var resp = await f.CreateClient().SendAsync(Req(id, tipo: "ENTRADA_ALMACEN"));
        var a = await Acuse(resp);

        Assert.NotEqual(HttpStatusCode.Accepted, resp.StatusCode);
        Assert.Equal(id, a.GetProperty("mensajeId").GetString());
        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Equal("tipo aún no soportado", a.GetProperty("error").GetString());

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var fila = await db.MensajesApp.AsNoTracking().SingleAsync(m => m.MensajeId == id);
        Assert.Equal("ERROR", fila.Estado);
        Assert.Equal("ENTRADA_ALMACEN", fila.Tipo);
        Assert.Equal("RQ-1", fila.Referencia);
    }
}
