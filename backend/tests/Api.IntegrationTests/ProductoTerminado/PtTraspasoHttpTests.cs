using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

/// <summary>Traspaso de PT entre sucursales (PT5.9): salida, tránsito y recepción total/parcial, contra el host real.</summary>
[Collection(ApiCompartida.Nombre)]
public class PtTraspasoHttpTests
{
    private const string Base = "/api/v1/producto-terminado";
    private static readonly string[] Operativo = [PermisosCanonicos.ProductoTerminadoPiezasVer, PermisosCanonicos.ProductoTerminadoExcepcionesGestionar];

    private readonly PtDatos _d;

    public PtTraspasoHttpTests(WebApplicationFactory<Program> factory) => _d = new PtDatos(factory);

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<(Guid A, Guid B, string[] Etiquetas)> PrepararAsync(int piezas)
    {
        var (a, b) = await _d.SucursalesAsync();
        var pedido = await _d.PedidoAsync(a, piezas);
        var etiquetas = new List<string>();
        for (var i = 1; i <= piezas; i++) etiquetas.Add(await _d.PiezaAsync(pedido, a, i));
        return (a, b, etiquetas.ToArray());
    }

    [Fact]
    public async Task CA5_10_salida_pasa_a_transito_y_la_recepcion_la_deja_en_el_PT_de_destino()
    {
        var (a, b, etiquetas) = await PrepararAsync(2);
        using var origen = await _d.ClienteAsync(Operativo, a);
        using var destino = await _d.ClienteAsync(Operativo, b);

        var salida = await origen.PostAsJsonAsync($"{Base}/traspasos", new { sucursalOrigenId = a, sucursalDestinoId = b, etiquetas });
        Assert.Equal(HttpStatusCode.OK, salida.StatusCode);
        var t = await Json(salida);
        Assert.Equal("EnTransito", t.GetProperty("estado").GetString());
        var id = t.GetProperty("id").GetGuid();

        // En tránsito: ya no cuenta en el PT de origen y el destino aún no la tiene.
        var enOrigen = await Json(await origen.GetAsync($"{Base}/piezas?sucursalId={a}&pageSize=200"));
        Assert.All(enOrigen.GetProperty("items").EnumerateArray().Where(x => etiquetas.Contains(x.GetProperty("etiqueta").GetString()!)),
            x => Assert.Equal("EnTransito", x.GetProperty("estado").GetString()));

        var recibido = await destino.PostAsync($"{Base}/traspasos/{id}/recibir", null);
        Assert.Equal(HttpStatusCode.OK, recibido.StatusCode);
        var r = await Json(recibido);
        Assert.Equal("Recibido", r.GetProperty("estado").GetString());
        Assert.Equal(0, r.GetProperty("pendientes").GetInt32());

        var enDestino = await Json(await destino.GetAsync($"{Base}/piezas?sucursalId={b}&pageSize=200"));
        var llegadas = enDestino.GetProperty("items").EnumerateArray().Where(x => etiquetas.Contains(x.GetProperty("etiqueta").GetString()!)).ToArray();
        Assert.Equal(2, llegadas.Length);
        Assert.All(llegadas, x => Assert.Equal("EnPT", x.GetProperty("estado").GetString()));
    }

    [Fact]
    public async Task PT5_9_a_recepcion_con_una_pieza_de_menos_queda_diferencia_pendiente_y_se_aclara_al_llegar()
    {
        var (a, b, etiquetas) = await PrepararAsync(2);
        using var origen = await _d.ClienteAsync(Operativo, a);
        using var destino = await _d.ClienteAsync(Operativo, b);
        var id = (await Json(await origen.PostAsJsonAsync($"{Base}/traspasos", new { sucursalOrigenId = a, sucursalDestinoId = b, etiquetas }))).GetProperty("id").GetGuid();
        var detalle = await Json(await destino.GetAsync($"{Base}/traspasos/{id}"));
        var piezas = detalle.GetProperty("piezas").EnumerateArray().Select(x => x.GetProperty("piezaId").GetGuid()).ToArray();

        var parcial = await Json(await destino.PostAsJsonAsync($"{Base}/traspasos/{id}/recibir", new { piezaIds = new[] { piezas[0] } }));
        Assert.Equal("ConDiferencia", parcial.GetProperty("estado").GetString());
        Assert.Equal(1, parcial.GetProperty("pendientes").GetInt32());

        // La pieza ya recibida no se recibe dos veces.
        var repetida = await destino.PostAsJsonAsync($"{Base}/traspasos/{id}/recibir", new { piezaIds = new[] { piezas[0] } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, repetida.StatusCode);

        var resto = await Json(await destino.PostAsync($"{Base}/traspasos/{id}/recibir", null));
        Assert.Equal("Recibido", resto.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Alcance_solo_el_origen_saca_y_solo_el_destino_recibe()
    {
        var (a, b, etiquetas) = await PrepararAsync(1);
        using var origen = await _d.ClienteAsync(Operativo, a);
        using var destino = await _d.ClienteAsync(Operativo, b);
        using var soloVer = await _d.ClienteAsync([PermisosCanonicos.ProductoTerminadoPiezasVer], a);

        var ajeno = await destino.PostAsJsonAsync($"{Base}/traspasos", new { sucursalOrigenId = a, sucursalDestinoId = b, etiquetas });
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await soloVer.PostAsJsonAsync($"{Base}/traspasos", new { sucursalOrigenId = a, sucursalDestinoId = b, etiquetas })).StatusCode);

        var id = (await Json(await origen.PostAsJsonAsync($"{Base}/traspasos", new { sucursalOrigenId = a, sucursalDestinoId = b, etiquetas }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await origen.PostAsync($"{Base}/traspasos/{id}/recibir", null)).StatusCode);
    }
}
