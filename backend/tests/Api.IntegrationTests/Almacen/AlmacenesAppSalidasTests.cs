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
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
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

    private const decimal Area = 8.9304m; // m² de la hoja 2440x3660 (AreaHoja.M2)

    private sealed record Sembrado(Guid ArticuloId, string ArticuloAw);

    private WebApplicationFactory<Program> ConLlave() =>
        _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:AlmacenesApp:ApiKey"] = Llave })));

    /// <summary>Artículo A+W con variante 2440x3660 y saldo de <paramref name="stock"/> hojas (en m², a 100 MXN/m²) en MID; sin variante, piezas.</summary>
    private async Task<Sembrado> SembrarAsync(decimal stock, bool conVariante = true, bool referenciaNumerica = false, string? refAw = null)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        refAw ??= referenciaNumerica ? $"9{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}" : $"SAL{n[..12]}";
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "Vidrio salida app", "M2");
        if (conVariante) prod.AplicarVariantes([new ProductoAwVarianteDato($"V-{n[..8]}", 3660m, 2440m, null, null)]);
        var art = new Articulo(Guid.CreateVersion7(), $"SAL-{n[12..24]}", "Vidrio salida app", conVariante ? "M2" : "PZA");
        art.AsignarProductoAw(prod.Id);
        compartido.AddRange(prod, art);
        await compartido.SaveChangesAsync();

        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        Guid? varianteId = conVariante
            ? await compartido.ProductosAwVariantes.AsNoTracking().Where(v => v.ProductoAwId == prod.Id).Select(v => v.Id).SingleAsync()
            : null;
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"S-{n[..16]}", "DEMO salida app", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "S", "DEMO S", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "S", "DEMO S");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art.Id, conVariante ? stock * Area : stock, 100m, varianteId));
        await db.SaveChangesAsync();
        return new(art.Id, refAw);
    }

    // 5-nov: el costo es el promedio de A+W por m² (costoM2); ya no hay costo total del grupo.
    private static object Grupo(string articuloAw, int piezas, int ancho = 2440, int alto = 3660, decimal? costoM2 = null) =>
        costoM2 is null
            ? new { articuloAw, ancho, alto, lote = "L1", piezas }
            : new { articuloAw, ancho, alto, lote = "L1", piezas, costoM2 };

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

    private sealed record Mov(TipoMovimiento Tipo, string? Motivo, Guid EmpresaId);

    private async Task<List<Mov>> MovsAsync(Guid articuloId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return await (from l in db.LineasMovimiento.AsNoTracking()
                      join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
                      where l.ArticuloId == articuloId
                      select new Mov(m.Tipo, m.Motivo, m.EmpresaId)).ToListAsync();
    }

    private async Task AssertErrorSinEfectosAsync(Sembrado s, object datos, string contiene, string tipo = "AJUSTE_SALIDA")
    {
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(id, tipo, datos)));
        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Contains(contiene, a.GetProperty("error").GetString());
        Assert.Equal(10 * Area, await DisponibleAsync(s.ArticuloId));
        Assert.Equal(0, await MovimientosAsync(s.ArticuloId));
        Assert.Equal("ERROR", await EstadoMensajeAsync(f, id));
    }

    /// <summary>Estado guardado del mensaje (A4.3-a: un rechazo queda en la bandeja con estado ERROR).</summary>
    private static async Task<string?> EstadoMensajeAsync(WebApplicationFactory<Program> f, string id)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return await db.MensajesApp.AsNoTracking().Where(m => m.MensajeId == id).Select(m => m.Estado).SingleOrDefaultAsync();
    }

    [Fact]
    public async Task CA4_4_AJUSTE_SALIDA_mesa_HEGLA1_con_3_hojas_baja_3_por_el_area_en_m2_y_acusa_RECIBIDO_con_folio()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 3, [Grupo(s.ArticuloAw, 3)], mesa: "HEGLA1"))));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.StartsWith("M-SAL", a.GetProperty("folio").GetString());
        Assert.Equal(7 * Area, await DisponibleAsync(s.ArticuloId));
        var mov = Assert.Single(await MovsAsync(s.ArticuloId));
        Assert.Equal(TipoMovimiento.SalidaConsumo, mov.Tipo);
        Assert.Contains("mesa HEGLA1", mov.Motivo);

        // El mismo mensajeId otra vez: DUPLICADO y el saldo no vuelve a bajar.
        var a2 = await Acuse(await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 3, [Grupo(s.ArticuloAw, 3)], mesa: "HEGLA1"))));
        Assert.Equal("DUPLICADO", a2.GetProperty("estado").GetString());
        Assert.Equal(a.GetProperty("folio").GetString(), a2.GetProperty("folio").GetString());
        Assert.Equal(7 * Area, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task Articulo_AW_desconocido_da_ERROR_sin_movimiento_y_queda_en_la_bandeja_con_estado_ERROR()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var resp = await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 3, [Grupo(s.ArticuloAw, 1), Grupo("NO-EXISTE-" + id[..8], 2)])));
        var a = await Acuse(resp);

        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Contains("NO-EXISTE-", a.GetProperty("error").GetString());
        Assert.Equal(10 * Area, await DisponibleAsync(s.ArticuloId));
        Assert.Equal(0, await MovimientosAsync(s.ArticuloId));
        Assert.Equal("ERROR", await EstadoMensajeAsync(f, id));
    }

    [Fact]
    public async Task A4_3_a_consumo_de_articulo_inexistente_va_a_la_bandeja_y_el_reenvio_tras_darlo_de_alta_descuenta_una_vez()
    {
        const string ruta = "/api/v1/almacen/mensajes-app/revision";
        var refAw = $"SALX{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}";
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var datos = DatosV6("AJUSTE", 3, [Grupo(refAw, 3)], mesa: "HEGLA1");

        var a = await Acuse(await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA", datos)));
        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Contains(refAw, a.GetProperty("error").GetString());

        var lector = await ContabTestKit.ClienteConPermisosAsync(f, PermisosCanonicos.AlmacenSalidasLeerTodas);
        try
        {
            var bandeja = await ContabTestKit.Json(await lector.GetAsync(ruta));
            var fila = Assert.Single(bandeja.EnumerateArray(), x => x.GetProperty("mensajeId").GetString() == id);
            Assert.Equal("ERROR", fila.GetProperty("estado").GetString());
            Assert.Contains(refAw, fila.GetProperty("error").GetString());

            // Se da de alta el artículo en el ERP y la app reenvía el mismo mensajeId: ahora descuenta.
            var s = await SembrarAsync(10, refAw: refAw);
            var a2 = await Acuse(await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA", datos)));
            Assert.Equal("RECIBIDO", a2.GetProperty("estado").GetString());
            Assert.Equal(7 * Area, await DisponibleAsync(s.ArticuloId));
            Assert.Equal(Millet.Almacen.Application.AppAlmacenes.SalidaAppAlmacenes.Recibido, await EstadoMensajeAsync(f, id));

            var despues = await ContabTestKit.Json(await lector.GetAsync(ruta));
            Assert.DoesNotContain(despues.EnumerateArray(), x => x.GetProperty("mensajeId").GetString() == id);

            // Un tercer envío ya es DUPLICADO y no vuelve a bajar.
            var a3 = await Acuse(await f.CreateClient().SendAsync(Req(id, "AJUSTE_SALIDA", datos)));
            Assert.Equal("DUPLICADO", a3.GetProperty("estado").GetString());
            Assert.Equal(7 * Area, await DisponibleAsync(s.ArticuloId));
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(lector);
        }
    }

    [Fact]
    public async Task Medida_girada_alto_por_ancho_coincide_con_la_variante()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 2, [Grupo(s.ArticuloAw, 2, ancho: 3660, alto: 2440)]))));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(8 * Area, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task Varios_grupos_con_uno_corto_acusa_ERROR_con_folio_y_motivo_queda_EN_REVISION_y_no_mueve_saldo()
    {
        var corto = await SembrarAsync(1);
        var sobrado = await SembrarAsync(10);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var datos = DatosV6("ROTURA", 4, [Grupo(sobrado.ArticuloAw, 2), Grupo(corto.ArticuloAw, 2)], motivo: "Rotura en mesa");
        var a = await Acuse(await f.CreateClient().SendAsync(Req(id, "ROTURA", datos)));

        // 5-nov (TI): ya no se acepta en silencio; ERROR con folio de revisión y el motivo.
        Assert.Equal("ERROR", a.GetProperty("estado").GetString());
        Assert.Equal($"REV-{id}", a.GetProperty("folio").GetString());
        Assert.Contains("Sin existencia suficiente", a.GetProperty("error").GetString());
        Assert.Equal(10 * Area, await DisponibleAsync(sobrado.ArticuloId));
        Assert.Equal(1 * Area, await DisponibleAsync(corto.ArticuloId));
        Assert.Equal(0, await MovimientosAsync(sobrado.ArticuloId) + await MovimientosAsync(corto.ArticuloId));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var fila = await db.MensajesApp.AsNoTracking().SingleAsync(m => m.MensajeId == id);
        Assert.Equal("EN_REVISION", fila.Estado);
        // Es la misma consulta que alimenta GET /api/v1/almacen/mensajes-app/revision (J6).
        Assert.Contains(await db.MensajesApp.AsNoTracking().Where(m => m.Estado == "EN_REVISION").Select(m => m.MensajeId).ToListAsync(), x => x == id);
        Assert.Equal($"REV-{id}", fila.Folio);

        // La app reprocesa el mismo mensajeId cuando ya hay existencia: se evalúa otra vez, descuenta y sale de la bandeja.
        var repuesto = await SembrarAsync(10, refAw: corto.ArticuloAw + "X");
        var a2 = await Acuse(await f.CreateClient().SendAsync(Req(id, "ROTURA",
            DatosV6("ROTURA", 4, [Grupo(sobrado.ArticuloAw, 2), Grupo(repuesto.ArticuloAw, 2)], motivo: "Rotura en mesa"))));
        Assert.Equal("RECIBIDO", a2.GetProperty("estado").GetString());
        Assert.Equal(8 * Area, await DisponibleAsync(sobrado.ArticuloId));
        // Y un tercer envío ya es DUPLICADO con el mismo folio, sin aplicar nada.
        var a3 = await Acuse(await f.CreateClient().SendAsync(Req(id, "ROTURA", datos)));
        Assert.Equal("DUPLICADO", a3.GetProperty("estado").GetString());
        Assert.Equal(a2.GetProperty("folio").GetString(), a3.GetProperty("folio").GetString());
        Assert.Equal(8 * Area, await DisponibleAsync(sobrado.ArticuloId));
    }

    [Fact]
    public async Task SALIDA_MANUAL_toma_costoM2_de_A_W_tal_cual_y_el_costo_de_la_hoja_y_del_grupo_salen_de_el()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL",
            DatosV6("MANUAL", 3, [Grupo(s.ArticuloAw, 3, costoM2: 20m)], motivo: "Muestra"))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        // costoM2 20 (≠ promedio del saldo, 100): se guarda tal cual; costo del grupo = 20 × 26.7912 m² = 535.824.
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var l = await db.LineasMovimiento.AsNoTracking().SingleAsync(x => x.ArticuloId == s.ArticuloId);
        Assert.Equal(20m, l.CostoUnitarioMxn);
        Assert.Equal(3 * Area, l.Cantidad);
        Assert.Equal(535.824m, l.CostoUnitarioMxn * l.Cantidad);
        Assert.Equal(TipoMovimiento.AjusteNegativo, Assert.Single(await MovsAsync(s.ArticuloId)).Tipo);
    }

    [Fact]
    public async Task SALIDA_MANUAL_sin_costo_usa_el_promedio_y_la_misma_regla_vale_si_nace_de_una_cancelacion_de_entrada()
    {
        var s = await SembrarAsync(10);
        var c = await SembrarAsync(10);
        await using var f = ConLlave();
        var sin = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL",
            DatosV6("MANUAL", 1, [Grupo(s.ArticuloAw, 1)], motivo: "Muestra"))));
        Assert.Equal("RECIBIDO", sin.GetProperty("estado").GetString());
        var cancelacion = new
        {
            movimiento = "CANC", sucursal = "MID", piezas = 1, motivo = "Cancelación de entrada", origen = "CANCELACION_ENTRADA",
            almacenes = new[] { new { almacenGeneral = new { id = 7, nombre = "OBRAS", tipo = "OBRAS" }, piezas = 1, grupos = new[] { Grupo(c.ArticuloAw, 1, costoM2: 37.5m) } } },
        };
        var canc = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL", cancelacion)));
        Assert.Equal("RECIBIDO", canc.GetProperty("estado").GetString());

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.Equal(100m, (await db.LineasMovimiento.AsNoTracking().SingleAsync(x => x.ArticuloId == s.ArticuloId)).CostoUnitarioMxn);
        Assert.Equal(37.5m, (await db.LineasMovimiento.AsNoTracking().SingleAsync(x => x.ArticuloId == c.ArticuloId)).CostoUnitarioMxn);
    }

    [Fact]
    public async Task SALIDA_MANUAL_con_medida_exige_costoM2_y_ya_no_acepta_el_costo_total_del_grupo()
    {
        var s = await SembrarAsync(10);
        var datos = DatosV6("MANUAL", 1, [new { articuloAw = s.ArticuloAw, ancho = 2440, alto = 3660, piezas = 1, costoPieza = 893.04m }], motivo: "Muestra");
        await AssertErrorSinEfectosAsync(s, datos, "costoM2", "SALIDA_MANUAL");
    }

    [Fact]
    public async Task SALIDA_MANUAL_sin_medida_toma_costoPieza_por_pieza()
    {
        var s = await SembrarAsync(10, conVariante: false);
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL",
            DatosV6("MANUAL", 4, [new { articuloAw = s.ArticuloAw, piezas = 4, costoPieza = 12.5m }], motivo: "Consumo"))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(6m, await DisponibleAsync(s.ArticuloId));
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var l = await db.LineasMovimiento.AsNoTracking().SingleAsync(x => x.ArticuloId == s.ArticuloId);
        Assert.Equal((12.5m, 4m), (l.CostoUnitarioMxn, l.Cantidad));
    }

    [Fact]
    public async Task AJUSTE_SALIDA_con_documentoPrevio_descuenta_al_llegar_la_validacion_y_lo_deja_en_el_motivo()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        // Entre el corte y la validación el ERP no tiene nada apartado: la hoja sigue disponible hasta que llega el mensaje.
        Assert.Equal(10 * Area, await DisponibleAsync(s.ArticuloId));
        var datos = new
        {
            movimiento = "AJUSTE", sucursal = "MID", piezas = 2, documentoPrevio = "DP-0077", mesa = "HEGLA1",
            almacenes = new[] { new { almacenGeneral = new { id = 1, nombre = "GENERAL", tipo = "GENERAL" }, piezas = 2, grupos = new[] { Grupo(s.ArticuloAw, 2) } } },
        };
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "AJUSTE_SALIDA", datos)));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(8 * Area, await DisponibleAsync(s.ArticuloId));
        Assert.Contains("documento previo DP-0077", Assert.Single(await MovsAsync(s.ArticuloId)).Motivo);
    }

    [Fact]
    public async Task AJUSTE_SALIDA_con_un_renglon_en_cero_da_ERROR_y_no_mueve_nada()
    {
        var s = await SembrarAsync(10);
        await AssertErrorSinEfectosAsync(s, DatosV6("AJUSTE", 0, [Grupo(s.ArticuloAw, 0)]), "piezas debe ser mayor que cero");
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
        Assert.Equal("ERROR", await EstadoMensajeAsync(f, id));
    }

    [Fact]
    public async Task ROTURA_con_existencia_es_BajaPorDano_con_el_motivo_y_baja_el_saldo()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "ROTURA",
            DatosV6("ROTURA", 2, [Grupo(s.ArticuloAw, 2)], motivo: "Se cayó del caballete"))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(8 * Area, await DisponibleAsync(s.ArticuloId));
        var mov = Assert.Single(await MovsAsync(s.ArticuloId));
        Assert.Equal(TipoMovimiento.BajaPorDano, mov.Tipo);
        Assert.Contains(": Se cayó del caballete", mov.Motivo);
    }

    [Fact]
    public async Task Material_por_cantidad_sin_medida_baja_el_saldo_sin_variante()
    {
        var s = await SembrarAsync(10, conVariante: false);
        await using var f = ConLlave();
        var grupo = new { articuloAw = s.ArticuloAw, lote = "L1", piezas = 4, ancho = (int?)null, alto = (int?)null, m2 = (decimal?)null };
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 4, [grupo]))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(6m, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task ArticuloAw_enviado_como_numero_JSON_se_resuelve()
    {
        var s = await SembrarAsync(10, referenciaNumerica: true);
        await using var f = ConLlave();
        var grupo = new { articuloAw = long.Parse(s.ArticuloAw), ancho = 2440, alto = 3660, lote = "L1", piezas = 2 };
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 2, [grupo]))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(8 * Area, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task Sin_datos_sucursal_da_ERROR_sin_efectos()
    {
        var s = await SembrarAsync(10);
        var datos = new
        {
            movimiento = "AJUSTE",
            almacenes = new[] { new { almacenGeneral = new { id = 1 }, piezas = 1, grupos = new[] { Grupo(s.ArticuloAw, 1) } } },
        };
        await AssertErrorSinEfectosAsync(s, datos, "datos.sucursal");
    }

    // H1 (86e3z5w25): el área y el equipo se resuelven por clave en la sucursal; una clave que no existe es ERROR sin mover nada.
    [Fact]
    public async Task Area_que_no_existe_en_la_sucursal_da_ERROR_sin_efectos()
    {
        var s = await SembrarAsync(10);
        var datos = new
        {
            movimiento = "AJUSTE", sucursal = "MID", piezas = 1, area = "NO-EXISTE-H1",
            almacenes = new[] { new { almacenGeneral = new { id = 1 }, piezas = 1, grupos = new[] { Grupo(s.ArticuloAw, 1) } } },
        };
        await AssertErrorSinEfectosAsync(s, datos, "El área NO-EXISTE-H1");
    }

    [Fact]
    public async Task Equipo_con_tipo_invalido_da_ERROR_sin_efectos()
    {
        var s = await SembrarAsync(10);
        var datos = new
        {
            movimiento = "AJUSTE", sucursal = "MID", piezas = 1, equipo = new { clave = "X" },
            almacenes = new[] { new { almacenGeneral = new { id = 1 }, piezas = 1, grupos = new[] { Grupo(s.ArticuloAw, 1) } } },
        };
        await AssertErrorSinEfectosAsync(s, datos, "datos.equipo");
    }

    [Fact]
    public async Task Piezas_cero_da_ERROR_sin_efectos()
    {
        var s = await SembrarAsync(10);
        await AssertErrorSinEfectosAsync(s, DatosV6("AJUSTE", 0, [Grupo(s.ArticuloAw, 0)]), "piezas debe ser mayor que cero");
    }

    [Fact]
    public async Task Solo_ancho_sin_alto_da_ERROR_sin_efectos()
    {
        var s = await SembrarAsync(10);
        var grupo = new { articuloAw = s.ArticuloAw, ancho = 2440, lote = "L1", piezas = 1 };
        await AssertErrorSinEfectosAsync(s, DatosV6("AJUSTE", 1, [grupo]), "deben venir juntos");
    }

    [Fact]
    public async Task Grupo_con_m2_que_no_coincide_con_piezas_por_area_da_ERROR_con_el_campo_sin_efectos()
    {
        var s = await SembrarAsync(10);
        var grupo = new { articuloAw = s.ArticuloAw, ancho = 2440, alto = 3660, lote = "L1", piezas = 3, m2 = 3m };
        await AssertErrorSinEfectosAsync(s, DatosV6("AJUSTE", 3, [grupo]), "almacenes[0].grupos[0].m2");
    }

    [Fact]
    public async Task Grupo_con_m2_coincidente_baja_ese_m2()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var grupo = new { articuloAw = s.ArticuloAw, ancho = 2440, alto = 3660, lote = "L1", piezas = 3, m2 = 26.7912m };
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "ROTURA",
            DatosV6("ROTURA", 3, [grupo], motivo: "Rotura"))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(7 * Area, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task Medida_inexistente_da_ERROR_sin_efectos()
    {
        var s = await SembrarAsync(10);
        await AssertErrorSinEfectosAsync(s, DatosV6("AJUSTE", 1, [Grupo(s.ArticuloAw, 1, ancho: 1111, alto: 2222)]), "No existe la medida");
    }

    [Fact]
    public async Task Sucursal_desconocida_da_ERROR_sin_efectos()
    {
        var s = await SembrarAsync(10);
        var datos = new
        {
            movimiento = "AJUSTE", sucursal = "NOEXISTE-SUC",
            almacenes = new[] { new { almacenGeneral = new { id = 1 }, piezas = 1, grupos = new[] { Grupo(s.ArticuloAw, 1) } } },
        };
        await AssertErrorSinEfectosAsync(s, datos, "No existe la sucursal");
    }

    [Fact]
    public async Task El_movimiento_se_guarda_con_la_empresa_del_usuario_de_servicio()
    {
        var s = await SembrarAsync(10);
        Guid empresaServicio;
        using (var scope = _factory.Services.CreateScope())
            empresaServicio = (await scope.ServiceProvider.GetRequiredService<IUsuarioServicioReadPort>()
                .ObtenerReordenAsync(CancellationToken.None))!.EmpresaId;
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "AJUSTE_SALIDA",
            DatosV6("AJUSTE", 1, [Grupo(s.ArticuloAw, 1)]))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.NotEqual(Guid.Empty, empresaServicio);
        Assert.Equal(empresaServicio, Assert.Single(await MovsAsync(s.ArticuloId)).EmpresaId);
    }

    [Fact]
    public async Task Bandeja_de_revision_401_anonimo_403_sin_permiso_y_200_con_el_mensaje_EN_REVISION()
    {
        const string ruta = "/api/v1/almacen/mensajes-app/revision";
        var corto = await SembrarAsync(1);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        await Acuse(await f.CreateClient().SendAsync(Req(id, "ROTURA",
            DatosV6("ROTURA", 2, [Grupo(corto.ArticuloAw, 2)], motivo: "Rotura"))));

        using var anonimo = await f.CreateClient().GetAsync(ruta);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);

        var sinPermiso = await ContabTestKit.ClienteConPermisosAsync(f, PermisosCanonicos.AlmacenEntradasLeer);
        var conPermiso = await ContabTestKit.ClienteConPermisosAsync(f, PermisosCanonicos.AlmacenSalidasLeerTodas);
        try
        {
            using var prohibido = await sinPermiso.GetAsync(ruta);
            Assert.Equal(HttpStatusCode.Forbidden, prohibido.StatusCode);

            var lista = await ContabTestKit.Json(await conPermiso.GetAsync(ruta));
            var fila = Assert.Single(lista.EnumerateArray(), x => x.GetProperty("mensajeId").GetString() == id);
            Assert.Equal("EN_REVISION", fila.GetProperty("estado").GetString());
            Assert.Equal($"REV-{id}", fila.GetProperty("folio").GetString());
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(sinPermiso);
            await ContabTestKit.LimpiarClienteAsync(conPermiso);
        }
    }

    [Fact]
    public async Task Lector_alm_mensaje_con_el_manejador_real_acusa_ERROR_con_folio_frena_la_referencia_y_sigue_con_las_demas()
    {
        var corto = await SembrarAsync(1);
        var otro = await SembrarAsync(10);
        var origen = _factory.Services.GetRequiredService<Millet.Integraciones.Aw.Infrastructure.AlmMensaje.SimuladoAlmMensajeOrigen>();
        var lector = _factory.Services.GetRequiredService<Millet.Almacen.Application.AppAlmacenes.AlmMensajeLector>();
        var n = Guid.NewGuid().ToString("N")[..10];
        string Sobre(string id, string tipo, string referencia, object datos) => JsonSerializer.Serialize(new
        {
            mensajeId = id, tipo, version = 1, fecha = "2026-11-05T10:00:00-06:00", origen = "AlmacenRQ_MP", referencia, datos,
        });
        var (m1, m2, m3) = ($"L1-{n}", $"L2-{n}", $"L3-{n}");
        // m1: pide 2 hojas y hay 1 (sin existencia); m2: misma referencia, sí alcanzaría; m3: otra referencia.
        origen.Encolar(m1, "SALIDA_MANUAL", $"RQ-{n}", Sobre(m1, "SALIDA_MANUAL", $"RQ-{n}",
            DatosV6("MANUAL", 2, [Grupo(corto.ArticuloAw, 2)], motivo: "Muestra")));
        origen.Encolar(m2, "AJUSTE_SALIDA", $"RQ-{n}", Sobre(m2, "AJUSTE_SALIDA", $"RQ-{n}",
            DatosV6("AJUSTE", 1, [Grupo(corto.ArticuloAw, 1)])));
        origen.Encolar(m3, "AJUSTE_SALIDA", $"RQ2-{n}", Sobre(m3, "AJUSTE_SALIDA", $"RQ2-{n}",
            DatosV6("AJUSTE", 1, [Grupo(otro.ArticuloAw, 1)])));

        var tick = await lector.ProcesarAsync(default);

        Assert.Equal(2, tick.Procesados);
        var a1 = origen.AcuseDe(m1)!;
        Assert.Equal(("ERROR", $"REV-{m1}"), (a1.Estado, a1.Folio));
        Assert.Contains("Sin existencia suficiente", a1.Error);
        Assert.Null(origen.AcuseDe(m2)); // espera: su referencia tiene un ERROR sin revisar
        Assert.Equal("RECIBIDO", origen.AcuseDe(m3)!.Estado);
        Assert.Equal(1 * Area, await DisponibleAsync(corto.ArticuloId)); // ni m1 ni m2 descontaron
        Assert.Equal(9 * Area, await DisponibleAsync(otro.ArticuloId));

        // Otro ciclo: m2 sigue esperando y el mismo m3 no se aplica otra vez.
        await lector.ProcesarAsync(default);
        Assert.Null(origen.AcuseDe(m2));
        Assert.Equal(9 * Area, await DisponibleAsync(otro.ArticuloId));
    }

    // ── M-159: ENTRADA_ALMACEN (contrato v8) ──────────────────────────────────

    /// <summary>ÚNICA activa de materiales directos en MID, para que la sucursal tenga almacén general aunque el seed no lo traiga.</summary>
    private async Task AsegurarAlmacenGeneralMidAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var mid = await compartido.Sucursales.AsNoTracking().Where(x => x.Clave == "MID").Select(x => x.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"E-{n[..16]}", "DEMO entrada app", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "E", "DEMO E", TipoSubAlmacen.MaterialesDirectos);
        db.AddRange(alm, sub, new Ubicacion(Guid.NewGuid(), sub.Id, "ÚNICA", "Almacén general", esDefault: true));
        await db.SaveChangesAsync();
    }

    private static object DatosEntrada(string entrada, string articuloAw, int piezas, int reclamadas = 0) => new
    {
        entrada, compra = "OC-NO-ERP", sucursal = "MID", piezas,
        lineas = new[]
        {
            new
            {
                requisicion = "RQ-1", posicion = 1, articuloAw, articuloAlmacenAw = 501, articulo = "6MM CLARO", ancho = 2440, alto = 3660,
                almacenGeneral = new { id = 2, nombre = "Conkal exportacion", tipo = "EXPORTACION", linea = "HEGLA 1" },
                piezas, reclamadas,
                grupos = new[] { Grupo(articuloAw, piezas) },
                almacenes = new[]
                {
                    new
                    {
                        almacenGeneral = new { id = 2, nombre = "Conkal exportacion", tipo = "EXPORTACION", linea = "HEGLA 1" },
                        piezas, grupos = new[] { Grupo(articuloAw, piezas) },
                    },
                },
            },
        },
    };

    [Fact]
    public async Task M159_ENTRADA_ALMACEN_sube_hojas_por_m2_al_almacen_general_sin_rack_y_el_reenvio_es_DUPLICADO()
    {
        await AsegurarAlmacenGeneralMidAsync();
        var s = await SembrarAsync(0);
        await using var f = ConLlave();
        var id = Guid.NewGuid().ToString();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(id, "ENTRADA_ALMACEN", DatosEntrada("EN-1", s.ArticuloAw, 10, reclamadas: 2))));

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.StartsWith("M-ENT", a.GetProperty("folio").GetString());
        Assert.Equal(10 * Area, await DisponibleAsync(s.ArticuloId)); // el total, incluidas las reclamadas
        var mov = Assert.Single(await MovsAsync(s.ArticuloId));
        Assert.Equal(TipoMovimiento.EntradaCompra, mov.Tipo);
        Assert.Contains("EN-1 compra OC-NO-ERP", mov.Motivo);
        using (var scope = f.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            var ubicacion = await (from l in db.LineasMovimiento.AsNoTracking()
                                   join u in db.Ubicaciones.AsNoTracking() on l.UbicacionId equals u.Id
                                   where l.ArticuloId == s.ArticuloId
                                   select u.EsDefault).SingleAsync();
            Assert.True(ubicacion); // sin rack: almacén general
        }

        var a2 = await Acuse(await f.CreateClient().SendAsync(Req(id, "ENTRADA_ALMACEN", DatosEntrada("EN-1", s.ArticuloAw, 10, reclamadas: 2))));
        Assert.Equal("DUPLICADO", a2.GetProperty("estado").GetString());
        Assert.Equal(a.GetProperty("folio").GetString(), a2.GetProperty("folio").GetString());
        Assert.Equal(10 * Area, await DisponibleAsync(s.ArticuloId));
    }

    [Fact]
    public async Task M159_ENTRADA_ALMACEN_de_articulo_desconocido_acusa_ERROR_con_folio_frena_su_referencia_y_no_mueve_saldo()
    {
        await AsegurarAlmacenGeneralMidAsync();
        var s = await SembrarAsync(0);
        var origen = _factory.Services.GetRequiredService<Millet.Integraciones.Aw.Infrastructure.AlmMensaje.SimuladoAlmMensajeOrigen>();
        var lector = _factory.Services.GetRequiredService<Millet.Almacen.Application.AppAlmacenes.AlmMensajeLector>();
        var n = Guid.NewGuid().ToString("N")[..10];
        string Sobre(string id, string referencia, object datos) => JsonSerializer.Serialize(new
        {
            mensajeId = id, tipo = "ENTRADA_ALMACEN", version = 1, fecha = "2026-11-09T10:00:00-06:00", origen = "AlmacenRQ_MP", referencia, datos,
        });
        var (m1, m2) = ($"E1-{n}", $"E2-{n}");
        origen.Encolar(m1, "ENTRADA_ALMACEN", $"EN-{n}", Sobre(m1, $"EN-{n}", DatosEntrada($"EN-{n}", "NO-EXISTE-" + n, 3)));
        origen.Encolar(m2, "ENTRADA_ALMACEN", $"EN-{n}", Sobre(m2, $"EN-{n}", DatosEntrada($"EN-{n}", s.ArticuloAw, 3)));

        await lector.ProcesarAsync(default);

        var a1 = origen.AcuseDe(m1)!;
        Assert.Equal(("ERROR", $"REV-{m1}"), (a1.Estado, a1.Folio));
        Assert.Contains("NO-EXISTE-", a1.Error);
        Assert.Null(origen.AcuseDe(m2)); // espera: su referencia tiene un ERROR sin revisar
        Assert.Equal(0m, await DisponibleAsync(s.ArticuloId));
        Assert.Equal(0, await MovimientosAsync(s.ArticuloId));
    }
}
