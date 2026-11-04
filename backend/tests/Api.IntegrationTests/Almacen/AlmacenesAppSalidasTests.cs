using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-MP-07: salidas de la app de Almacenes con payloads del contrato v6 (§7.1, §8.3, §8.5) por
/// POST /api/v1/integraciones/almacenes/mensajes. Cada prueba siembra su propio artículo A+W (clave única),
/// variante y saldo en la sucursal MID; no toca datos de otras pruebas.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class AlmacenesAppSalidasTests
{
    private const string Ruta = "/api/v1/integraciones/almacenes/mensajes";
    private const string Llave = "llave-test-almacenes";
    private readonly WebApplicationFactory<Program> _factory;

    public AlmacenesAppSalidasTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private sealed record Sembrado(Guid ArticuloId, string ArticuloAw);

    private WebApplicationFactory<Program> ConLlave() =>
        _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:AlmacenesApp:ApiKey"] = Llave })));

    /// <summary>Artículo A+W con variante 2440x3660 y saldo de <paramref name="stock"/> piezas a 100 MXN en MID.</summary>
    private async Task<Sembrado> SembrarAsync(decimal stock)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var refAw = $"SAL{n[..12]}";
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "Vidrio salida app", "M2");
        prod.AplicarVariantes([new ProductoAwVarianteDato($"V-{n[..8]}", 3660m, 2440m, null, null)]);
        var art = new Articulo(Guid.CreateVersion7(), $"SAL-{n[12..24]}", "Vidrio salida app", "PZA");
        art.AsignarProductoAw(prod.Id);
        compartido.AddRange(prod, art);
        await compartido.SaveChangesAsync();

        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var varianteId = await compartido.ProductosAwVariantes.AsNoTracking()
            .Where(v => v.ProductoAwId == prod.Id).Select(v => v.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"S-{n[..16]}", "DEMO salida app", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "S", "DEMO S", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "S", "DEMO S");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art.Id, stock, 100m, varianteId));
        await db.SaveChangesAsync();
        return new(art.Id, refAw);
    }

    private static object Grupo(string articuloAw, int piezas, int ancho = 2440, int alto = 3660, decimal? costo = null) =>
        costo is null
            ? new { articuloAw, ancho, alto, lote = "L1", piezas }
            : new { articuloAw, ancho, alto, lote = "L1", piezas, costo };

    private static HttpRequestMessage Req(string id, string tipo, object datos)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(new
            {
                mensajeId = id, tipo, version = 1, fecha = "2026-10-06T17:47:00-06:00",
                origen = "AlmacenRQ_MP", referencia = "SAL-1", datos,
            }),
        };
        r.Headers.Add("X-Mensaje-Id", id);
        r.Headers.Add("X-Millet-Almacenes-Key", Llave);
        return r;
    }

    private static object DatosV6(string movimiento, int piezas, object[] grupos, string? mesa = null, string? motivo = null) => new
    {
        movimiento, sucursal = "MID", piezas, motivo, mesa, jobid = "J-1", pedidoAw = "P-9",
        almacenes = new[] { new { almacenGeneral = new { id = 1, nombre = "GENERAL", tipo = "GENERAL" }, piezas, grupos } },
    };

    private static readonly int[] AlmacenesMalFormado = [1];

    private static async Task<JsonElement> Acuse(HttpResponseMessage r)
    {
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.True(cuerpo.Contains("\"estado\""), $"HTTP {(int)r.StatusCode}: {cuerpo}");
        return JsonDocument.Parse(cuerpo).RootElement.Clone();
    }

    private async Task<decimal> DisponibleAsync(Guid articuloId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return await db.SaldosInventario.AsNoTracking().Where(s => s.ArticuloId == articuloId).SumAsync(s => s.CantidadDisponible);
    }

    private async Task<int> MovimientosAsync(Guid articuloId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return await db.LineasMovimiento.AsNoTracking().CountAsync(l => l.ArticuloId == articuloId);
    }

    [Fact]
    public async Task CA4_4_AJUSTE_SALIDA_mesa_HEGLA1_con_3_hojas_baja_3_y_acusa_RECIBIDO_con_folio()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 3, [Grupo(s.ArticuloAw, 3)], mesa: "HEGLA1"))));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.StartsWith("M-SAL", a.GetProperty("folio").GetString());
        Assert.Equal(7m, await DisponibleAsync(s.ArticuloId));

        // El mismo mensajeId otra vez: DUPLICADO y el saldo no vuelve a bajar.
        var a2 = await Acuse(await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 3, [Grupo(s.ArticuloAw, 3)], mesa: "HEGLA1"))));
        Assert.Equal("DUPLICADO", a2.GetProperty("estado").GetString());
        Assert.Equal(a.GetProperty("folio").GetString(), a2.GetProperty("folio").GetString());
        Assert.Equal(7m, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task Articulo_AW_desconocido_da_ERROR_sin_movimiento_y_no_guarda_el_mensaje()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var resp = await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 3, [Grupo(s.ArticuloAw, 1), Grupo("NO-EXISTE-" + id[..8], 2)])));
        var a = await Acuse(resp);

        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Contains("NO-EXISTE-", a.GetProperty("error").GetString());
        Assert.Equal(10m, await DisponibleAsync(s.ArticuloId));
        Assert.Equal(0, await MovimientosAsync(s.ArticuloId));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.False(await db.MensajesApp.AnyAsync(m => m.MensajeId == id));
    }

    [Fact]
    public async Task Medida_girada_alto_por_ancho_coincide_con_la_variante()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 2, [Grupo(s.ArticuloAw, 2, ancho: 3660, alto: 2440)]))));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(8m, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task Varios_grupos_con_uno_corto_acusa_RECIBIDO_queda_EN_REVISION_y_no_mueve_saldo()
    {
        var corto = await SembrarAsync(1);
        var sobrado = await SembrarAsync(10);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(id, "ROTURA",
            DatosV6("ROTURA", 4, [Grupo(sobrado.ArticuloAw, 2), Grupo(corto.ArticuloAw, 2)], motivo: "Rotura en mesa"))));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(10m, await DisponibleAsync(sobrado.ArticuloId));
        Assert.Equal(1m, await DisponibleAsync(corto.ArticuloId));
        Assert.Equal(0, await MovimientosAsync(sobrado.ArticuloId) + await MovimientosAsync(corto.ArticuloId));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var fila = await db.MensajesApp.AsNoTracking().SingleAsync(m => m.MensajeId == id);
        Assert.Equal("EN_REVISION", fila.Estado);
        // Es la misma consulta que alimenta GET /api/v1/almacen/mensajes-app/revision (J6).
        Assert.Contains(await db.MensajesApp.AsNoTracking().Where(m => m.Estado == "EN_REVISION").Select(m => m.MensajeId).ToListAsync(), x => x == id);
    }

    [Fact]
    public async Task SALIDA_MANUAL_con_costo_300_de_grupo_y_3_piezas_deja_la_linea_a_100()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL",
            DatosV6("MANUAL", 3, [Grupo(s.ArticuloAw, 3, costo: 450m)], motivo: "Muestra"))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        // Costo del grupo 450 (≠ promedio 100·3) → unitario 150: prueba que se usa el costo del mensaje.
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            var l = await db.LineasMovimiento.AsNoTracking().SingleAsync(x => x.ArticuloId == s.ArticuloId);
            Assert.Equal(150m, l.CostoUnitarioMxn);
        }

        var s2 = await SembrarAsync(10);
        var a2 = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL",
            DatosV6("MANUAL", 3, [Grupo(s2.ArticuloAw, 3, costo: 300m)], motivo: "Muestra"))));
        Assert.Equal("RECIBIDO", a2.GetProperty("estado").GetString());
        using var scope2 = _factory.Services.CreateScope();
        using var bypass2 = scope2.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db2 = scope2.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.Equal(100m, (await db2.LineasMovimiento.AsNoTracking().SingleAsync(x => x.ArticuloId == s2.ArticuloId)).CostoUnitarioMxn);
    }

    [Fact]
    public async Task Payload_con_almacenes_mal_formado_da_ERROR_con_el_campo()
    {
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var resp = await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA",
            new { movimiento = "AJUSTE", sucursal = "MID", almacenes = AlmacenesMalFormado }));
        var a = await Acuse(resp);

        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Contains("almacenes[0]", a.GetProperty("error").GetString());
        Assert.NotEqual(HttpStatusCode.InternalServerError, resp.StatusCode);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.False(await db.MensajesApp.AnyAsync(m => m.MensajeId == id));
    }
}
