using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>Contrato HTTP, permisos y alcance por sucursal de Producto Terminado (ADR-0051), contra el host real.</summary>
[Collection(ApiCompartida.Nombre)]
public class PtHttpTests
{
    private const string Base = "/api/v1/producto-terminado";
    private static readonly string[] Ver = [PermisosCanonicos.ProductoTerminadoPiezasVer];
    private static readonly string[] Gestionar = [PermisosCanonicos.ProductoTerminadoExcepcionesGestionar];
    private static readonly string[] Operativo = [PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoExcepcionesGestionar];
    private static readonly string[] Corporativo =
    [
        PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales,
        PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales,
    ];

    private readonly WebApplicationFactory<Program> _factory;
    private readonly PtDatos _d;

    public PtHttpTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _d = new PtDatos(factory);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> Post(HttpClient c, Guid id, string accion, int? version, object? body = null, string? key = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/escaneos/{id}/{accion}");
        if (body is not null) req.Content = JsonContent.Create(body);
        if (version is int v) req.Headers.TryAddWithoutValidation("If-Match", $"\"{v}\"");
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return c.SendAsync(req);
    }

    private static async Task<JsonElement[]> Escaneos(HttpClient c, string prefijoPedido, string? resultado = null)
    {
        var r = await c.GetAsync($"{Base}/escaneos?pageSize=200" + (resultado is null ? "" : $"&resultado={resultado}"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).GetProperty("items").EnumerateArray().Where(x => x.GetProperty("numeroPedido").GetString()!.StartsWith(prefijoPedido, StringComparison.Ordinal)).ToArray();
    }

    [Fact]
    public async Task M28_Filtro_devuelve_las_excepciones_y_sin_filtro_las_abiertas_con_motivo_e_intentos()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var tag = PtDatos.Nuevo();
        var p1 = await _d.EscaneoAsync("Excepcion", a, tag + "-1", motivo: "PT_PIEZA_EXCEDE_POSICION", intentos: 2);
        var p2 = await _d.EscaneoAsync("Excepcion", a, tag + "-2");
        var p3 = await _d.EscaneoAsync("Pospuesto", a, tag + "-3", intentos: 1);
        var p4 = await _d.EscaneoAsync("Reposicion", a, tag + "-4");

        var filtradas = await Escaneos(corp, tag, "Excepcion");
        Assert.Equal(2, filtradas.Length);
        Assert.All(filtradas, x => Assert.Equal("Excepcion", x.GetProperty("resultado").GetString()));

        var todas = await Escaneos(corp, tag);
        Assert.Equal(4, todas.Length);
        var fila = todas.Single(x => x.GetProperty("id").GetGuid() == p1.Id);
        Assert.Equal("PT_PIEZA_EXCEDE_POSICION", fila.GetProperty("motivo").GetString());
        Assert.Equal(2, fila.GetProperty("intentos").GetInt32());
        Assert.Equal(1, todas.Single(x => x.GetProperty("id").GetGuid() == p3.Id).GetProperty("intentos").GetInt32());
        _ = (p2, p4);
    }

    [Fact]
    public async Task M29_Reintento_con_causa_corregida_crea_una_pieza_y_repite_con_la_misma_llave()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var esc = await _d.EscaneoAsync("Excepcion", a);
        await _d.FacturacionAsync(esc.Pedido, a, 3);
        var key = Guid.NewGuid().ToString();

        var r1 = await Post(corp, esc.Id, "reintentar", esc.Version, key: key);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var j1 = await Json(r1);
        Assert.Equal("Alta", j1.GetProperty("resultado").GetString());
        var etiqueta = (await _d.LeerEscaneoAsync(esc.Id)).Etiqueta;
        Assert.Equal(1, await _d.PiezasPorEtiquetaAsync(etiqueta));

        var r2 = await Post(corp, esc.Id, "reintentar", esc.Version, key: key);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(j1.ToString(), (await Json(r2)).ToString());
        Assert.Equal(1, await _d.PiezasPorEtiquetaAsync(etiqueta));
    }

    [Fact]
    public async Task M30_Reintento_sin_corregir_la_causa_conserva_resultado_y_suma_intento()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var esc = await _d.EscaneoAsync("Excepcion", a, numeroPieza: 9, motivo: "PT_PIEZA_EXCEDE_POSICION", intentos: 1);
        await _d.FacturacionAsync(esc.Pedido, a, 3);

        var r = await Post(corp, esc.Id, "reintentar", esc.Version);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var j = await Json(r);
        Assert.Equal("Excepcion", j.GetProperty("resultado").GetString());
        Assert.Equal("PT_PIEZA_EXCEDE_POSICION", j.GetProperty("motivo").GetString());
        Assert.Equal(2, j.GetProperty("intentos").GetInt32());
        Assert.Equal(2, (await _d.LeerEscaneoAsync(esc.Id)).Intentos);
    }

    [Fact]
    public async Task M31_Descartar_con_motivo_resuelve_la_fila_y_no_crea_pieza()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var esc = await _d.EscaneoAsync("Excepcion", a);

        var r = await Post(corp, esc.Id, "descartar", esc.Version, new { motivo = "Etiqueta de prueba" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.NotNull(r.Headers.ETag);
        var j = await Json(r);
        Assert.Equal("Etiqueta de prueba", j.GetProperty("motivoResolucion").GetString());
        Assert.NotEqual(JsonValueKind.Null, j.GetProperty("resueltoAt").ValueKind);
        Assert.Empty(await Escaneos(corp, esc.Pedido));
        Assert.Null((await _d.LeerEscaneoAsync(esc.Id)).PiezaId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task M32_Descartar_sin_motivo_devuelve_400_con_campo_motivo(string motivo)
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var esc = await _d.EscaneoAsync("Excepcion", a);

        var r = await Post(corp, esc.Id, "descartar", esc.Version, new { motivo });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("motivo", (await r.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
        Assert.Null((await _d.LeerEscaneoAsync(esc.Id)).ResueltoAt);
    }

    [Fact]
    public async Task M33_Revisar_aplica_a_reposicion_y_da_422_en_excepcion()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var repo = await _d.EscaneoAsync("Reposicion", a);
        var exc = await _d.EscaneoAsync("Excepcion", a);

        Assert.Equal(HttpStatusCode.OK, (await Post(corp, repo.Id, "revisar", repo.Version)).StatusCode);
        Assert.NotNull((await _d.LeerEscaneoAsync(repo.Id)).ResueltoAt);
        var r = await Post(corp, exc.Id, "revisar", exc.Version);
        Assert.Equal((HttpStatusCode)422, r.StatusCode);
        Assert.Contains("PT_REVISION_NO_APLICA", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task M34_Doble_descarte_409_id_desconocido_404_version_vieja_409_y_sin_if_match_428()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var esc = await _d.EscaneoAsync("Excepcion", a);

        var primero = await Post(corp, esc.Id, "descartar", esc.Version, new { motivo = "uno" });
        Assert.Equal(HttpStatusCode.OK, primero.StatusCode);
        var nueva = (await Json(primero)).GetProperty("version").GetInt32();
        var doble = await Post(corp, esc.Id, "descartar", nueva, new { motivo = "dos" });
        Assert.Equal(HttpStatusCode.Conflict, doble.StatusCode);
        Assert.Contains("PT_ESCANEO_YA_RESUELTO", await doble.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await Post(corp, Guid.NewGuid(), "descartar", 1, new { motivo = "x" })).StatusCode);

        var otro = await _d.EscaneoAsync("Excepcion", a);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(corp, otro.Id, "descartar", otro.Version + 7, new { motivo = "x" })).StatusCode);
        var sin = await Post(corp, otro.Id, "descartar", null, new { motivo = "x" });
        Assert.Equal((HttpStatusCode)428, sin.StatusCode);
        Assert.Null((await _d.LeerEscaneoAsync(otro.Id)).ResueltoAt);
    }

    [Fact]
    public async Task M36_Post_sin_Idempotency_Key_devuelve_400()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var corp = await _d.ClienteAsync(Corporativo);
        var esc = await _d.EscaneoAsync("Excepcion", a);
        using var crudo = _factory.CreateClient();
        crudo.DefaultRequestHeaders.Authorization = corp.DefaultRequestHeaders.Authorization;

        var r = await Post(crudo, esc.Id, "descartar", esc.Version, new { motivo = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Null((await _d.LeerEscaneoAsync(esc.Id)).ResueltoAt);
    }

    [Fact]
    public async Task M37_Operativo_lista_solo_su_sucursal_y_pedir_otra_da_403()
    {
        var (a, b) = await _d.SucursalesAsync();
        var pedido = await _d.PedidoAsync(a);
        await _d.PiezaAsync(pedido, a, 1);
        await _d.PiezaAsync(pedido, b, 2);
        using var op = await _d.ClienteAsync(Operativo, a);

        var r = await op.GetAsync($"{Base}/piezas?pedido={pedido}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var items = (await Json(r)).GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal(a, items[0].GetProperty("sucursalId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await op.GetAsync($"{Base}/piezas?pedido={pedido}&sucursalId={b}")).StatusCode);
    }

    [Fact]
    public async Task M38_Con_ver_todas_sin_sucursal_ve_ambas()
    {
        var (a, b) = await _d.SucursalesAsync();
        var pedido = await _d.PedidoAsync(a);
        await _d.PiezaAsync(pedido, a, 1);
        await _d.PiezaAsync(pedido, b, 2);
        using var corp = await _d.ClienteAsync([PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales]);

        var j = await Json(await corp.GetAsync($"{Base}/piezas?pedido={pedido}"));
        Assert.Equal(2, j.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task M39_Operativo_no_actua_sobre_escaneo_de_otra_sucursal_por_id()
    {
        var (a, b) = await _d.SucursalesAsync();
        using var op = await _d.ClienteAsync(Operativo, a);
        var esc = await _d.EscaneoAsync("Excepcion", b);

        foreach (var accion in new[] { "reintentar", "descartar", "revisar" })
        {
            var r = await Post(op, esc.Id, accion, esc.Version, accion == "descartar" ? new { motivo = "x" } : null);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        var db = await _d.LeerEscaneoAsync(esc.Id);
        Assert.Null(db.ResueltoAt);
        Assert.Equal(0, db.Intentos);
        Assert.Equal(esc.Version, db.Version);
    }

    [Fact]
    public async Task M40_Escaneo_sin_sucursal_operativo_403_no_listado_y_corporativo_lo_descarta()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var op = await _d.ClienteAsync(Operativo, a);
        using var corp = await _d.ClienteAsync(Corporativo);
        var esc = await _d.EscaneoAsync("Excepcion", null, motivo: "PT_SUCURSAL_SIN_CLAVE_AW");

        var r = await Post(op, esc.Id, "descartar", esc.Version, new { motivo = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("SUCURSAL_NO_DETERMINADA", await r.Content.ReadAsStringAsync());
        Assert.Empty(await Escaneos(op, esc.Pedido));

        Assert.Single(await Escaneos(corp, esc.Pedido));
        Assert.Equal(HttpStatusCode.OK, (await Post(corp, esc.Id, "descartar", esc.Version, new { motivo = "sin sucursal" })).StatusCode);
    }

    [Fact]
    public async Task M41_Sin_permisos_PT_devuelve_403_en_las_cuatro_rutas()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var c = await _d.ClienteAsync([], a);
        var esc = await _d.EscaneoAsync("Excepcion", a);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Base}/piezas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Base}/escaneos")).StatusCode);
        foreach (var accion in new[] { "reintentar", "descartar", "revisar" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, esc.Id, accion, esc.Version, accion == "descartar" ? new { motivo = "x" } : null)).StatusCode);
        Assert.Null((await _d.LeerEscaneoAsync(esc.Id)).ResueltoAt);
    }

    [Fact]
    public async Task M42_Con_solo_piezas_ver_lista_piezas_pero_las_acciones_dan_403()
    {
        var (a, _) = await _d.SucursalesAsync();
        using var c = await _d.ClienteAsync(Ver, a);
        var esc = await _d.EscaneoAsync("Excepcion", a);

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{Base}/piezas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Base}/escaneos")).StatusCode);
        foreach (var accion in new[] { "reintentar", "descartar", "revisar" })
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, esc.Id, accion, esc.Version, accion == "descartar" ? new { motivo = "x" } : null)).StatusCode);
        Assert.Null((await _d.LeerEscaneoAsync(esc.Id)).ResueltoAt);
        _ = Gestionar;
    }

    [Fact]
    public async Task M43_Sin_token_o_con_token_invalido_devuelve_401()
    {
        using var c = _factory.CreateClientWithIdempotency();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync($"{Base}/piezas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, Guid.NewGuid(), "descartar", 1, new { motivo = "x" })).StatusCode);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync($"{Base}/escaneos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, Guid.NewGuid(), "revisar", 1)).StatusCode);
    }

    [Fact]
    public async Task M44_Pieza_de_A_con_pedido_vendido_desde_B_se_lista_para_A_y_no_para_B()
    {
        var (a, b) = await _d.SucursalesAsync();
        var pedido = await _d.PedidoAsync(sucursalVenta: b);
        await _d.PiezaAsync(pedido, a, 1);
        using var opA = await _d.ClienteAsync(Ver, a);
        using var opB = await _d.ClienteAsync(Ver, b);

        Assert.Equal(1, (await Json(await opA.GetAsync($"{Base}/piezas?pedido={pedido}"))).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await Json(await opB.GetAsync($"{Base}/piezas?pedido={pedido}"))).GetProperty("items").GetArrayLength());
    }
}
