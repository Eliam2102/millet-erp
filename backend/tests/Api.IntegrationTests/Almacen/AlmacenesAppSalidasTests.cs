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
    private async Task<Sembrado> SembrarAsync(decimal stock, bool conVariante = true, bool referenciaNumerica = false)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var refAw = referenciaNumerica ? $"9{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}" : $"SAL{n[..12]}";
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
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        Assert.False(await db.MensajesApp.AnyAsync(m => m.MensajeId == id));
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
        Assert.Equal(10 * Area, await DisponibleAsync(s.ArticuloId));
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
        Assert.Equal(8 * Area, await DisponibleAsync(s.ArticuloId));
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
        Assert.Equal(10 * Area, await DisponibleAsync(sobrado.ArticuloId));
        Assert.Equal(1 * Area, await DisponibleAsync(corto.ArticuloId));
        Assert.Equal(0, await MovimientosAsync(sobrado.ArticuloId) + await MovimientosAsync(corto.ArticuloId));
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var fila = await db.MensajesApp.AsNoTracking().SingleAsync(m => m.MensajeId == id);
        Assert.Equal("EN_REVISION", fila.Estado);
        // Es la misma consulta que alimenta GET /api/v1/almacen/mensajes-app/revision (J6).
        Assert.Contains(await db.MensajesApp.AsNoTracking().Where(m => m.Estado == "EN_REVISION").Select(m => m.MensajeId).ToListAsync(), x => x == id);
    }

    [Fact]
    public async Task SALIDA_MANUAL_con_costo_de_grupo_y_3_hojas_calcula_el_unitario_por_m2()
    {
        var s = await SembrarAsync(10);
        await using var f = ConLlave();
        var a = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL",
            DatosV6("MANUAL", 3, [Grupo(s.ArticuloAw, 3, costo: 535.824m)], motivo: "Muestra"))));
        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        // 3 hojas = 26.7912 m²; costo del grupo 535.824 (≠ promedio) → 20 MXN/m²: el unitario es costo/m², no costo/piezas.
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            var l = await db.LineasMovimiento.AsNoTracking().SingleAsync(x => x.ArticuloId == s.ArticuloId);
            Assert.Equal(20m, l.CostoUnitarioMxn);
            Assert.Equal(3 * Area, l.Cantidad);
        }
        Assert.Equal(TipoMovimiento.AjusteNegativo, Assert.Single(await MovsAsync(s.ArticuloId)).Tipo);

        var s2 = await SembrarAsync(10);
        var a2 = await Acuse(await f.CreateClient().SendAsync(Req(Guid.NewGuid().ToString(), "SALIDA_MANUAL",
            DatosV6("MANUAL", 3, [Grupo(s2.ArticuloAw, 3, costo: 2679.12m)], motivo: "Muestra"))));
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
            Assert.Contains(lista.EnumerateArray(), x => x.GetProperty("mensajeId").GetString() == id);
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(sinPermiso);
            await ContabTestKit.LimpiarClienteAsync(conPermiso);
        }
    }
}
