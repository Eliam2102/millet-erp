using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.Persistence.Migrations;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// M-446 contra Postgres (trigger del ledger real): la carga de las 16 llaves corre dos veces sin duplicar; CAMBIO_ALMACEN de
/// 10 hojas HEGLA 1 → BYSTRONIC con 25 en el origen deja 15 y 10, un folio, RECIBIDO y ningún evento contable; 30 hojas con 25
/// es ERROR REV-… sin mover nada; el mismo mensajeId dos veces es DUPLICADO sin segundo movimiento; y el CAMBIO_ALMACEN real de la
/// base de ensayo (9-nov) se procesa con su catálogo armado. Cada prueba usa su propia sucursal (clave única) para que sus llaves
/// no cambien el comportamiento de las demás sucursales (sin llaves = comportamiento previo).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class AlmacenesAppCambioAlmacenTests(WebApplicationFactory<Program> factory)
{
    private const string Ruta = "/api/v1/integraciones/almacenes/mensajes";
    private const string Llave = "llave-test-almacenes";
    private static readonly decimal Hoja = AreaHoja.M2(3302m, 6100m);
    private static readonly decimal HojaChica = AreaHoja.M2(1800m, 2100m);

    private sealed record Escenario(string Sucursal, Guid Articulo, string ArticuloAw, Dictionary<string, (Guid Ubicacion, Guid Sub)> Llaves);

    private WebApplicationFactory<Program> ConLlave() =>
        factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:AlmacenesApp:ApiKey"] = Llave })));

    /// <summary>Sucursal nueva con sus 16 llaves (la carga de la migración, corrida dos veces) y un vidrio de A+W con dos medidas.</summary>
    private async Task<Escenario> EscenarioAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var clave = $"K{n[..9]}";
        var empresa = await cat.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.EmpresaId).SingleAsync();
        cat.Add(new Sucursal(Guid.NewGuid(), empresa, clave, $"Sucursal M-446 {clave}", TipoSucursal.Planta,
            "Calle prueba", "1", "Centro", "Conkal", "Conkal", "Yucatán", "97345", "México"));
        var refAw = $"9{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "6MM CLARO M-446", "M2");
        prod.AplicarVariantes([new ProductoAwVarianteDato($"V-{n[..8]}", 6100m, 3302m, null, null),
            new ProductoAwVarianteDato($"W-{n[..8]}", 2100m, 1800m, null, null)]);
        var art = new Articulo(Guid.CreateVersion7(), $"M446-{n[10..22]}", "6MM CLARO M-446", "M2");
        art.AsignarProductoAw(prod.Id);
        cat.AddRange(prod, art);
        await cat.SaveChangesAsync();

        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        await db.Database.ExecuteSqlRawAsync(M446AlmacenesAppLlaves.SembrarSql(clave));
        await db.Database.ExecuteSqlRawAsync(M446AlmacenesAppLlaves.SembrarSql(clave)); // idempotente
        var sucursalId = await cat.Sucursales.AsNoTracking().Where(s => s.Clave == clave).Select(s => s.Id).SingleAsync();
        var llaves = await (from a in db.AlmacenesApp.AsNoTracking()
                            join u in db.Ubicaciones.AsNoTracking() on a.UbicacionId equals u.Id
                            where a.SucursalId == sucursalId
                            select new { a.TipoAlmacen, a.Linea, u.Id, u.SubAlmacenId }).ToListAsync();
        return new(clave, art.Id, refAw, llaves.ToDictionary(x => $"{x.TipoAlmacen}|{x.Linea}", x => (x.Id, x.SubAlmacenId)));
    }

    private async Task SaldoAsync(Escenario e, string llave, decimal cantidad, decimal ancho, decimal alto, string lote)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var prodId = await cat.Articulos.AsNoTracking().Where(a => a.Id == e.Articulo).Select(a => a.ProductoAwId).SingleAsync();
        var variante = await cat.ProductosAwVariantes.AsNoTracking()
            .Where(v => v.ProductoAwId == prodId && ((v.AnchoMm == ancho && v.AltoMm == alto) || (v.AnchoMm == alto && v.AltoMm == ancho)))
            .Select(v => v.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var (ubicacion, sub) = e.Llaves[llave];
        db.SaldosInventario.Add(new SaldoInventario(ubicacion, sub, e.Articulo, cantidad, 285.4m, variante, lote));
        await db.SaveChangesAsync();
    }

    private async Task<decimal> DisponibleAsync(Escenario e, string llave)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var ubicacion = e.Llaves[llave].Ubicacion;
        return await db.SaldosInventario.AsNoTracking().Where(s => s.ArticuloId == e.Articulo && s.UbicacionId == ubicacion)
            .SumAsync(s => s.CantidadDisponible);
    }

    private static object Cambio(Escenario e, int hojas, string movimiento = "MV-1") => new
    {
        movimiento, sucursal = e.Sucursal, motivo = "Traspaso de HEGLA 1 a BYSTRONIC", piezas = hojas,
        almacenDestino = new { id = 1, nombre = "Conkal exportacion", tipo = "EXPORTACION", linea = "BYSTRONIC" },
        almacenes = new[]
        {
            new
            {
                almacenGeneral = new { id = 1, nombre = "Conkal exportacion", tipo = "EXPORTACION", linea = "HEGLA 1" }, piezas = hojas,
                grupos = new[] { new { articuloAw = long.Parse(e.ArticuloAw), ancho = 3302m, alto = 6100m, lote = "L-1", piezas = hojas, m2 = hojas * Hoja } },
            },
        },
    };

    private static async Task<JsonElement> EnviarAsync(HttpClient c, string id, string tipo, object datos, string referencia = "MV-1")
    {
        var r = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(new { mensajeId = id, tipo, version = 1, fecha = "2026-11-09T17:29:04-06:00", origen = "AlmacenRQ_MP", referencia, datos }),
        };
        r.Headers.Add("X-Mensaje-Id", id);
        r.Headers.Add("X-Millet-Almacenes-Key", Llave);
        var resp = await c.SendAsync(r);
        var cuerpo = await resp.Content.ReadAsStringAsync();
        Assert.True(cuerpo.Contains("\"estado\""), $"HTTP {(int)resp.StatusCode}: {cuerpo}");
        return JsonDocument.Parse(cuerpo).RootElement.Clone();
    }

    private sealed record Mov(Guid Id, TipoMovimiento Tipo, string? Folio, Guid? SalidaOrigenId, Guid Ubicacion, decimal Cantidad, decimal Costo, string? Lote);

    private async Task<List<Mov>> MovsAsync(Guid articulo)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return await (from l in db.LineasMovimiento.AsNoTracking()
                      join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
                      where l.ArticuloId == articulo
                      select new Mov(m.Id, m.Tipo, m.Folio, m.SalidaOrigenId, l.UbicacionId!.Value, l.Cantidad, l.CostoUnitarioMxn, l.Lote)).ToListAsync();
    }

    private async Task<long> EventosOutboxAsync(IEnumerable<Guid> movimientos)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            long total = 0;
            foreach (var id in movimientos)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT count(*) FROM almacen.integration_events_outbox WHERE payload::text LIKE '%{id}%'";
                total += (long)(await cmd.ExecuteScalarAsync())!;
            }
            return total;
        }
        finally { await conn.CloseAsync(); }
    }

    [Fact]
    public async Task M446_la_carga_de_las_16_llaves_corre_dos_veces_sin_duplicar()
    {
        var e = await EscenarioAsync();

        Assert.Equal(16, e.Llaves.Count);
        Assert.Single(e.Llaves.Values.Select(v => v.Sub).Distinct());
        Assert.Contains("IMPORTACION TEMPORAL|HEGLA 2", e.Llaves.Keys);
        Assert.Contains("OBRAS|", e.Llaves.Keys); // herraje sin línea
    }

    [Fact]
    public async Task M446_CAMBIO_ALMACEN_10_hojas_HEGLA_1_a_BYSTRONIC_con_25_mueve_10_un_folio_sin_poliza_y_el_reenvio_es_DUPLICADO()
    {
        var e = await EscenarioAsync();
        await SaldoAsync(e, "EXPORTACION|HEGLA 1", 25 * Hoja, 3302m, 6100m, "L-1");
        await using var f = ConLlave();
        var c = f.CreateClient();
        var id = Guid.NewGuid().ToString();

        var a = await EnviarAsync(c, id, "CAMBIO_ALMACEN", Cambio(e, 10));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        var folio = a.GetProperty("folio").GetString()!;
        Assert.StartsWith("M-TRA", folio);
        Assert.DoesNotContain(",", folio);
        Assert.Equal(15 * Hoja, await DisponibleAsync(e, "EXPORTACION|HEGLA 1"));
        Assert.Equal(10 * Hoja, await DisponibleAsync(e, "EXPORTACION|BYSTRONIC"));

        // Kardex: salida TRA (el folio del acuse) y su entrada TRE ligada, mismo costo y lote; sin evento contable.
        var movs = await MovsAsync(e.Articulo);
        var salida = Assert.Single(movs, m => m.Tipo == TipoMovimiento.TrasladoInterno);
        var entrada = Assert.Single(movs, m => m.Tipo == TipoMovimiento.EntradaTrasladoInterno);
        Assert.Equal(folio, salida.Folio);
        Assert.Equal(salida.Id, entrada.SalidaOrigenId);
        Assert.Equal((285.4m, "L-1"), (entrada.Costo, entrada.Lote));
        Assert.Equal(0, await EventosOutboxAsync([salida.Id, entrada.Id]));

        // Criterio 5: el mismo mensajeId otra vez → DUPLICADO con el mismo folio y ningún movimiento más.
        var a2 = await EnviarAsync(c, id, "CAMBIO_ALMACEN", Cambio(e, 10));
        Assert.Equal(("DUPLICADO", folio), (a2.GetProperty("estado").GetString(), a2.GetProperty("folio").GetString()));
        Assert.Equal(2, (await MovsAsync(e.Articulo)).Count);
        Assert.Equal(15 * Hoja, await DisponibleAsync(e, "EXPORTACION|HEGLA 1"));
    }

    [Fact]
    public async Task M446_CAMBIO_ALMACEN_30_hojas_con_25_da_ERROR_REV_y_no_mueve_nada()
    {
        var e = await EscenarioAsync();
        await SaldoAsync(e, "EXPORTACION|HEGLA 1", 25 * Hoja, 3302m, 6100m, "L-1");
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();

        var a = await EnviarAsync(f.CreateClient(), id, "CAMBIO_ALMACEN", Cambio(e, 30));

        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Equal($"REV-{id}", a.GetProperty("folio").GetString());
        Assert.Contains("Sin existencia suficiente", a.GetProperty("error").GetString());
        Assert.Equal(25 * Hoja, await DisponibleAsync(e, "EXPORTACION|HEGLA 1"));
        Assert.Equal(0m, await DisponibleAsync(e, "EXPORTACION|BYSTRONIC"));
        Assert.Empty(await MovsAsync(e.Articulo));
        // Queda EN_REVISION: el lector frena los siguientes de su referencia hasta el reenvío.
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.Equal("EN_REVISION", await db.MensajesApp.AsNoTracking().Where(m => m.MensajeId == id).Select(m => m.Estado).SingleAsync());
    }

    [Fact]
    public async Task M446_llave_desconocida_da_ERROR_REV()
    {
        var e = await EscenarioAsync();
        var id = Guid.NewGuid().ToString();
        await using var f = ConLlave();
        var datos = JsonSerializer.Serialize(Cambio(e, 1)).Replace("\"linea\":\"BYSTRONIC\"", "\"linea\":\"HEGLA 9\"");

        var a = await EnviarAsync(f.CreateClient(), id, "CAMBIO_ALMACEN", JsonDocument.Parse(datos).RootElement);

        Assert.Equal(("ERROR", $"REV-{id}"), (a.GetProperty("estado").GetString(), a.GetProperty("folio").GetString()));
        Assert.Contains("EXPORTACION HEGLA 9", a.GetProperty("error").GetString());
    }

    [Fact]
    public async Task M446_CAMBIO_ALMACEN_real_de_la_base_de_ensayo_pasa_de_EXPORTACION_a_OBRAS()
    {
        var e = await EscenarioAsync();
        await SaldoAsync(e, "EXPORTACION|BYSTRONIC", 2 * HojaChica, 1800m, 2100m, "zz_arnes_CA1");
        // MV-0014368 tal como lo escribió la app el 9-nov; solo cambian la sucursal y el artículo A+W por los de la prueba.
        var real = """
{"movimiento": "MV-0014368", "sucursal": "CONKAL", "motivo": "zz_arnes pasan a obras", "piezas": 2, "almacenDestino": {"id": 2, "nombre": "Conkal obras", "tipo": "OBRAS", "linea": "BYSTRONIC"}, "almacenes": [{"almacenGeneral": {"id": 1, "nombre": "Conkal exportacion", "tipo": "EXPORTACION", "linea": "BYSTRONIC"}, "piezas": 2, "grupos": [{"articuloAw": 100006, "articuloAlmacenAw": 4549, "ancho": 1800.0, "alto": 2100.0, "lote": "zz_arnes_CA1", "pedimento": null, "etiqueta": "zz_arnes_E1", "piezas": 2, "m2": 7.56}]}]}
""".Replace("CONKAL", e.Sucursal).Replace("100006", e.ArticuloAw);
        await using var f = ConLlave();

        var a = await EnviarAsync(f.CreateClient(), Guid.NewGuid().ToString(), "CAMBIO_ALMACEN", JsonDocument.Parse(real).RootElement, "MV-0014368");

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(0m, await DisponibleAsync(e, "EXPORTACION|BYSTRONIC"));
        Assert.Equal(2 * HojaChica, await DisponibleAsync(e, "OBRAS|BYSTRONIC"));
    }
}
