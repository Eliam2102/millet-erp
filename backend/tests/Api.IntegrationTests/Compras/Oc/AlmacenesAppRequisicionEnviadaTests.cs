using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// ALM-APP-5: REQUISICION_ENVIADA por el endpoint de mensajes de la app de Almacenes crea la
/// RQ (EnSurtido, proveedor fijo) y la OC sale con ese proveedor. Sin cleanup (mismo criterio
/// que el resto de la clase): folios externos y claves A+W son únicos por corrida.
/// </summary>
public partial class OrdenesCompraEndpointsTests
{
    private const string RutaMensajesApp = "/api/v1/integraciones/almacenes/mensajes";
    private const string LlaveApp = "llave-test-almacenes";

    private sealed record SembradoApp(Guid ProveedorId, int ClaveAw, string ArticuloAw);

    private async Task<SembradoApp> SembrarProveedorYArticuloAppAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var claveAw = Random.Shared.Next(1_000_000, 2_000_000_000);
        var prov = new Proveedor(Guid.CreateVersion7(), $"APP-{n[..10]}", $"Proveedor app {n[..6]}", "AAA010101AAA",
            TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        prov.AsignarClaveAw(claveAw);
        var refAw = $"APP{n[..12]}";
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "Vidrio de prueba app", "M2");
        var art = new Articulo(Guid.CreateVersion7(), $"APP-{n[10..22]}", "Vidrio de prueba app", "PZA");
        art.AsignarProductoAw(prod.Id);
        db.AddRange(prov, prod, art);
        await db.SaveChangesAsync();
        return new(prov.Id, claveAw, refAw);
    }

    private HttpClient ClienteMensajesApp() =>
        _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:AlmacenesApp:ApiKey"] = LlaveApp })))
            .CreateClient();

    private static async Task<JsonElement> EnviarRqAppAsync(HttpClient c, string mensajeId, string folioExterno, int claveAw, string articuloAw)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, RutaMensajesApp)
        {
            Content = JsonContent.Create(new
            {
                mensajeId, tipo = "REQUISICION_ENVIADA", version = 1, fecha = "2026-10-06T17:47:00-06:00",
                origen = "AlmacenRQ_MP", referencia = folioExterno,
                datos = new
                {
                    folio = folioExterno, sucursal = "MID",
                    proveedor = new { claveAw, nombre = "3M", moneda = (string?)null },
                    fechaRequerida = "2026-10-13", comentario = "Pedido urgente",
                    lineas = new[]
                    {
                        new { posicion = 1, articuloAw, articulo = "6MM CLARO", articuloAlmacenAw = 501,
                              ancho = 3302, alto = 6100, piezas = 13, obraAw = (int?)null, obra = "Torre Norte" },
                    },
                },
            }),
        };
        r.Headers.Add("X-Mensaje-Id", mensajeId);
        r.Headers.Add("X-Millet-Almacenes-Key", LlaveApp);
        var resp = await c.SendAsync(r);
        Assert.NotEqual(System.Net.HttpStatusCode.Accepted, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Requisicion?> RqPorFolioExternoAsync(string folioExterno)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones.AsNoTracking()
            .Include(r => r.Lineas)
            .SingleOrDefaultAsync(r => r.Origen == OrigenRequisicion.AlmacenesApp && r.FolioExterno == folioExterno);
    }

    [Fact]
    public async Task AlmApp5_Nueva_crea_RQ_EnSurtido_con_folio_y_la_OC_sale_con_el_mismo_proveedor()
    {
        var s = await SembrarProveedorYArticuloAppAsync();
        var folioExt = $"RQ-{Guid.NewGuid():N}"[..20];
        var ack = await EnviarRqAppAsync(ClienteMensajesApp(), Guid.NewGuid().ToString(), folioExt, s.ClaveAw, s.ArticuloAw);

        Assert.Equal("RECIBIDO", ack.GetProperty("estado").GetString());
        var folio = ack.GetProperty("folio").GetString();
        Assert.False(string.IsNullOrWhiteSpace(folio));

        var rq = await RqPorFolioExternoAsync(folioExt);
        Assert.NotNull(rq);
        Assert.Equal(folio, rq.Folio.Valor);
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        Assert.Equal(s.ProveedorId, rq.ProveedorSugeridoId);
        var l = Assert.Single(rq.Lineas);
        Assert.Equal((13m, 13m, 0m), (l.Cantidad, l.CantidadDeCompra, l.CantidadDeAlmacen));
        Assert.Equal((13, 3302, 6100), (l.Piezas, l.AnchoMm, l.AltoMm));
        Assert.Equal("Torre Norte", l.Proyecto);
        Assert.Equal(new DateOnly(2026, 10, 13), l.FechaRequerida);

        var client = await CreateSuperAdminClientAsync();
        var oc = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqP2(rq.Id) with { ProveedorId = s.ProveedorId });
        Assert.True(oc.IsSuccessStatusCode, await oc.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AlmApp5_OC_con_otro_proveedor_falla_OC_PROVEEDOR_FIJO_APP()
    {
        var s = await SembrarProveedorYArticuloAppAsync();
        var folioExt = $"RQ-{Guid.NewGuid():N}"[..20];
        await EnviarRqAppAsync(ClienteMensajesApp(), Guid.NewGuid().ToString(), folioExt, s.ClaveAw, s.ArticuloAw);
        var rq = await RqPorFolioExternoAsync(folioExt);

        var client = await CreateSuperAdminClientAsync();
        var oc = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqP2(rq!.Id)); // ProveedorActivoId ≠ proveedor de la RQ
        await AssertCodigoP2(oc, System.Net.HttpStatusCode.UnprocessableEntity, "OC_PROVEEDOR_FIJO_APP");
    }

    [Fact]
    public async Task AlmApp5_Mismo_mensaje_dos_veces_y_mismo_folio_con_otro_mensajeId_dan_DUPLICADO_con_el_primer_folio()
    {
        var s = await SembrarProveedorYArticuloAppAsync();
        var c = ClienteMensajesApp();
        var folioExt = $"RQ-{Guid.NewGuid():N}"[..20];
        var id = Guid.NewGuid().ToString();
        var a1 = await EnviarRqAppAsync(c, id, folioExt, s.ClaveAw, s.ArticuloAw);
        var a2 = await EnviarRqAppAsync(c, id, folioExt, s.ClaveAw, s.ArticuloAw);
        var a3 = await EnviarRqAppAsync(c, Guid.NewGuid().ToString(), folioExt, s.ClaveAw, s.ArticuloAw);

        Assert.Equal("RECIBIDO", a1.GetProperty("estado").GetString());
        foreach (var a in new[] { a2, a3 })
        {
            Assert.Equal("DUPLICADO", a.GetProperty("estado").GetString());
            Assert.Equal(a1.GetProperty("folio").GetString(), a.GetProperty("folio").GetString());
        }
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones
            .CountAsync(r => r.Origen == OrigenRequisicion.AlmacenesApp && r.FolioExterno == folioExt));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AlmApp5_Proveedor_o_articulo_desconocido_da_ERROR_sin_crear_RQ(bool proveedorDesconocido)
    {
        var s = await SembrarProveedorYArticuloAppAsync();
        var folioExt = $"RQ-{Guid.NewGuid():N}"[..20];
        var ack = await EnviarRqAppAsync(ClienteMensajesApp(), Guid.NewGuid().ToString(), folioExt,
            proveedorDesconocido ? 2_100_000_000 : s.ClaveAw, proveedorDesconocido ? s.ArticuloAw : "NO-EXISTE-AW");

        Assert.Equal("ERROR", ack.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, ack.GetProperty("folio").ValueKind);
        Assert.Contains(proveedorDesconocido ? "proveedor" : "artículo", ack.GetProperty("error").GetString());
        Assert.Null(await RqPorFolioExternoAsync(folioExt));
    }
}
