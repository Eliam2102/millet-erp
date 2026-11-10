using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>Hoja de salida y albarán (PT5.5): carga, emisión única, salida real en Facturación y embarque parcial, contra el host real.</summary>
[Collection(ApiCompartida.Nombre)]
public class PtHojaSalidaHttpTests
{
    private const string Base = "/api/v1/producto-terminado";
    private static readonly string[] Operativo = [PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoExcepcionesGestionar];

    private readonly PtDatos _d;

    public PtHojaSalidaHttpTests(WebApplicationFactory<Program> factory) => _d = new PtDatos(factory);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<(Guid Sucursal, string Numero, Guid PedidoFacturableId, string[] Etiquetas)> PrepararAsync(int piezas)
    {
        var (a, _) = await _d.SucursalesAsync();
        var (numero, facturable) = await _d.PedidoLiberadoAsync(a, piezas);
        var etiquetas = new List<string>();
        for (var i = 1; i <= piezas; i++) etiquetas.Add(await _d.PiezaAsync(numero, a, i));
        return (a, numero, facturable, etiquetas.ToArray());
    }

    private static object Hoja(Guid sucursal, string numero, IEnumerable<string> etiquetas) =>
        new { sucursalId = sucursal, destino = "Obra Mérida norte", foranea = false, pedidos = new[] { new { numeroPedido = numero, etiquetas } } };

    [Fact]
    public async Task PT5_5_a_emitir_descuenta_una_vez_registra_la_salida_en_Facturacion_y_emitir_otra_vez_no_repite()
    {
        var (a, numero, facturable, etiquetas) = await PrepararAsync(3);
        using var cliente = await _d.ClienteAsync(Operativo, a);

        var creada = await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas));
        Assert.Equal(HttpStatusCode.OK, creada.StatusCode);
        var id = (await Json(creada)).GetProperty("id").GetGuid();
        Assert.Empty(await _d.SalidasFacturacionAsync(facturable));

        await _d.AutorizarSalidaAsync(id, a);
        var emitida = await Json(await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null));
        Assert.Equal("Emitida", emitida.GetProperty("estado").GetString());
        Assert.False(emitida.GetProperty("yaEmitida").GetBoolean());
        Assert.Equal([3m], await _d.SalidasFacturacionAsync(facturable));

        var otraVez = await Json(await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null));
        Assert.True(otraVez.GetProperty("yaEmitida").GetBoolean());
        Assert.Equal([3m], await _d.SalidasFacturacionAsync(facturable));

        var piezas = await Json(await cliente.GetAsync($"{Base}/piezas?pedido={numero}&sucursalId={a}&pageSize=50"));
        Assert.All(piezas.GetProperty("items").EnumerateArray(), x => Assert.Equal("Embarcada", x.GetProperty("estado").GetString()));
        var avance = piezas.GetProperty("avance")[0];
        Assert.Equal(3, avance.GetProperty("entregadas").GetInt32());
        Assert.Equal(0, avance.GetProperty("cargadas").GetInt32());
    }

    [Fact]
    public async Task CA5_13_embarcar_parte_del_pedido_factura_solo_lo_embarcado_y_el_resto_queda_pendiente()
    {
        var (a, numero, facturable, etiquetas) = await PrepararAsync(5);
        using var cliente = await _d.ClienteAsync(Operativo, a);

        var id = (await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas.Take(3))))).GetProperty("id").GetGuid();
        await _d.AutorizarSalidaAsync(id, a);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null)).StatusCode);
        Assert.Equal([3m], await _d.SalidasFacturacionAsync(facturable));

        var piezas = await Json(await cliente.GetAsync($"{Base}/piezas?pedido={numero}&sucursalId={a}&pageSize=50"));
        var avance = piezas.GetProperty("avance")[0];
        Assert.Equal(3, avance.GetProperty("entregadas").GetInt32());
        Assert.Equal(2, avance.GetProperty("enPt").GetInt32());

        // Las que quedaban salen en otra hoja del mismo pedido.
        var resto = (await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas.Skip(3))))).GetProperty("id").GetGuid();
        await _d.AutorizarSalidaAsync(resto, a);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync($"{Base}/hojas-salida/{resto}/emitir", null)).StatusCode);
        Assert.Equal([3m, 2m], await _d.SalidasFacturacionAsync(facturable));
    }

    [Fact]
    public async Task Reglas_9nov_sin_factura_no_se_emite_y_quien_armo_la_hoja_no_se_autoriza_a_si_mismo()
    {
        var (a, numero, _, etiquetas) = await PrepararAsync(2);
        using var cliente = await _d.ClienteAsync([.. Operativo, PermisosCanonicos.ProductoTerminadoSalidaAprobarAdministracion], a);
        var id = (await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas)))).GetProperty("id").GetGuid();

        var bloqueada = await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bloqueada.StatusCode);
        Assert.Contains("PT_SALIDA_SIN_FACTURA", await bloqueada.Content.ReadAsStringAsync());
        var reglas = (await Json(await cliente.GetAsync($"{Base}/hojas-salida/{id}"))).GetProperty("reglas");
        Assert.Equal($"{numero}:2", reglas.GetProperty("sinFactura")[0].GetString());

        var propia = await cliente.PostAsJsonAsync($"{Base}/hojas-salida/{id}/autorizar", new { nivel = "administracion", motivo = "Cliente urgente, factura en proceso" });
        Assert.Contains("PT_SALIDA_SEPARACION_FUNCIONES", await propia.Content.ReadAsStringAsync());

        await _d.AutorizarSalidaAsync(id, a);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null)).StatusCode);
    }

    [Fact]
    public async Task Una_hoja_en_carga_se_cancela_y_una_emitida_no()
    {
        var (a, numero, _, etiquetas) = await PrepararAsync(2);
        using var cliente = await _d.ClienteAsync(Operativo, a);
        var id = (await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas)))).GetProperty("id").GetGuid();

        var cancelada = await Json(await cliente.PostAsync($"{Base}/hojas-salida/{id}/cancelar", null));
        Assert.Equal("Cancelada", cancelada.GetProperty("estado").GetString());

        var otra = (await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas)))).GetProperty("id").GetGuid();
        await _d.AutorizarSalidaAsync(otra, a);
        await cliente.PostAsync($"{Base}/hojas-salida/{otra}/emitir", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PostAsync($"{Base}/hojas-salida/{otra}/cancelar", null)).StatusCode);
    }

    [Fact]
    public async Task Alcance_solo_quien_opera_la_sucursal_arma_y_emite()
    {
        var (a, numero, _, etiquetas) = await PrepararAsync(1);
        var (_, b) = await _d.SucursalesAsync();
        using var ajeno = await _d.ClienteAsync(Operativo, b);
        using var soloVer = await _d.ClienteAsync([PermisosCanonicos.ProductoTerminadoPiezasVer], a);

        Assert.Equal(HttpStatusCode.Forbidden, (await ajeno.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVer.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas))).StatusCode);
    }

    [Fact]
    public async Task PT5_4_carga_pieza_por_pieza_con_una_rota_el_albaran_y_la_salida_llevan_solo_lo_cargado()
    {
        var (a, numero, facturable, etiquetas) = await PrepararAsync(3);
        var (_, _, _, ajenas) = await PrepararAsync(1);
        using var cliente = await _d.ClienteAsync(Operativo, a);

        var ruta = await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, [])));
        var id = ruta.GetProperty("id").GetGuid();
        Assert.Equal(3, ruta.GetProperty("albaranes")[0].GetProperty("disponibles").GetArrayLength());

        async Task<HttpResponseMessage> Pieza(string accion, string etiqueta) => await cliente.PostAsJsonAsync($"{Base}/hojas-salida/{id}/{accion}", new { etiqueta });
        Assert.Equal(HttpStatusCode.OK, (await Pieza("cargar", etiquetas[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pieza("cargar", etiquetas[1])).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pieza("cargar", ajenas[0])).StatusCode);
        var rota = await Json(await Pieza("rota", etiquetas[2]));
        Assert.Equal(etiquetas[2], rota.GetProperty("albaranes")[0].GetProperty("rotas")[0].GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pieza("cargar", etiquetas[2])).StatusCode);

        await _d.AutorizarSalidaAsync(id, a);
        var emitida = await Json(await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null));
        Assert.Equal(2, emitida.GetProperty("albaranes")[0].GetProperty("total").GetInt32());
        Assert.Equal([2m], await _d.SalidasFacturacionAsync(facturable));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Pieza("descargar", etiquetas[0])).StatusCode);
    }

    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Fact]
    public async Task PT5_6_entrega_pide_firma_o_evidencia_y_guarda_parcial_con_rechazo_en_sitio()
    {
        var (a, numero, _, etiquetas) = await PrepararAsync(3);
        using var cliente = await _d.ClienteAsync(Operativo, a);
        var id = (await Json(await cliente.PostAsJsonAsync($"{Base}/hojas-salida", Hoja(a, numero, etiquetas)))).GetProperty("id").GetGuid();
        await _d.AutorizarSalidaAsync(id, a);
        var emitida = await Json(await cliente.PostAsync($"{Base}/hojas-salida/{id}/emitir", null));
        var albaran = emitida.GetProperty("albaranes")[0].GetProperty("id").GetGuid();
        var url = $"{Base}/hojas-salida/{id}/albaranes/{albaran}/entrega";

        // PT5.6-a: sin firma ni foto el ERP no confirma.
        var sin = await cliente.PostAsJsonAsync(url, new { receptor = "Mariana Pech" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sin.StatusCode);

        var ok = await Json(await cliente.PostAsJsonAsync(url, new { receptor = "Mariana Pech", firma = Png, rechazadas = new[] { etiquetas[2] }, motivoRechazo = "Vidrio con rayón" }));
        var entrega = ok.GetProperty("albaranes")[0].GetProperty("entrega");
        Assert.Equal("Parcial", entrega.GetProperty("resultado").GetString());
        Assert.Equal(2, entrega.GetProperty("entregadas").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await cliente.PostAsJsonAsync(url, new { receptor = "Mariana Pech", firma = Png })).StatusCode);

        var evidencia = await Json(await cliente.GetAsync($"{url}/evidencia"));
        Assert.StartsWith("data:image/png;base64,", evidencia.GetProperty("firma").GetString());

        var piezas = await Json(await cliente.GetAsync($"{Base}/piezas?pedido={numero}&sucursalId={a}&pageSize=50"));
        var estados = piezas.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("estado").GetString()!).Order().ToArray();
        Assert.Equal("EnRevision,Entregada,Entregada", string.Join(',', estados));
    }
}
