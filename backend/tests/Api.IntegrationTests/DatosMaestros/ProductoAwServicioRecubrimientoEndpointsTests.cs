using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.DatosMaestros.Application.ProductosAw;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>PIN-2: esServicioRecubrimiento en crear, editar, detalle, lista (+filtro) y que la sincronización A+W no la pisa.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ProductoAwServicioRecubrimientoEndpointsTests(WebApplicationFactory<Program> factory)
{
    private const string Base = "/api/v1/datos-maestros/productos-aw";
    private static string NuevaRef() => "SR" + Guid.NewGuid().ToString("N")[..10];

    private static async Task<Guid> CrearAsync(HttpClient http, string referencia, bool? marca)
    {
        var r = await http.PostAsJsonAsync(Base, new
        {
            referenciaExterna = referencia, descripcion = "PROD " + referencia, unidadMedida = "M2",
            esServicioRecubrimiento = marca,
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> DetalleAsync(HttpClient http, Guid id) =>
        await http.GetFromJsonAsync<JsonElement>($"{Base}/{id}");

    private static async Task PatchAsync(HttpClient http, Guid id, object body) =>
        Assert.Equal(HttpStatusCode.NoContent,
            (await http.PatchAsJsonAsync($"{Base}/{id}", body)).StatusCode);

    private static AplicarProductoAwSnapshot Snap(string referencia, string descripcion, int segundos = 0) => new(
        referencia, descripcion, "PZA", false, [], DateTime.UtcNow.AddSeconds(segundos), "1", "m1");

    private async Task AplicarAsync(AplicarProductoAwSnapshot s)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AplicarProductoAwService>().AplicarAsync(s, CancellationToken.None);
    }

    [Fact]
    public async Task Crear_sin_el_campo_queda_false_y_con_true_queda_true_en_detalle()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var sin = await CrearAsync(http, NuevaRef(), null);
        var con = await CrearAsync(http, NuevaRef(), true);

        Assert.False((await DetalleAsync(http, sin)).GetProperty("esServicioRecubrimiento").GetBoolean());
        Assert.True((await DetalleAsync(http, con)).GetProperty("esServicioRecubrimiento").GetBoolean());
    }

    [Fact]
    public async Task Patch_null_no_cambia_y_bool_asigna()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var id = await CrearAsync(http, NuevaRef(), true);

        await PatchAsync(http, id, new { descripcion = "NUEVA" });
        Assert.True((await DetalleAsync(http, id)).GetProperty("esServicioRecubrimiento").GetBoolean());

        await PatchAsync(http, id, new { esServicioRecubrimiento = false });
        Assert.False((await DetalleAsync(http, id)).GetProperty("esServicioRecubrimiento").GetBoolean());

        await PatchAsync(http, id, new { esServicioRecubrimiento = true });
        Assert.True((await DetalleAsync(http, id)).GetProperty("esServicioRecubrimiento").GetBoolean());
    }

    [Fact]
    public async Task Lista_trae_el_campo_y_filtra_por_esServicioRecubrimiento()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var pref = NuevaRef();
        var si = await CrearAsync(http, pref + "A", true);
        var no = await CrearAsync(http, pref + "B", false);

        async Task<List<(Guid Id, bool Marca)>> Listar(string filtro)
        {
            var j = await http.GetFromJsonAsync<JsonElement>($"{Base}?referencia={pref}{filtro}&limit=50");
            return j.GetProperty("items").EnumerateArray()
                .Select(e => (e.GetProperty("id").GetGuid(), e.GetProperty("esServicioRecubrimiento").GetBoolean())).ToList();
        }

        var todos = await Listar("");
        Assert.Contains((si, true), todos);
        Assert.Contains((no, false), todos);
        Assert.Equal([(si, true)], await Listar("&esServicioRecubrimiento=true"));
        Assert.Equal([(no, false)], await Listar("&esServicioRecubrimiento=false"));
    }

    [Fact]
    public async Task Sync_crea_con_false_y_al_actualizar_no_pisa_la_marca_del_erp()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var r = NuevaRef();
        await AplicarAsync(Snap(r, "AW 1"));

        var lista = await http.GetFromJsonAsync<JsonElement>($"{Base}?referencia={r}");
        var item = Assert.Single(lista.GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("esServicioRecubrimiento").GetBoolean());
        var id = item.GetProperty("id").GetGuid();

        await PatchAsync(http, id, new { esServicioRecubrimiento = true });
        await AplicarAsync(Snap(r, "AW 2", segundos: 1));

        var d = await DetalleAsync(http, id);
        Assert.True(d.GetProperty("esServicioRecubrimiento").GetBoolean());
        Assert.Equal("AW 2", d.GetProperty("descripcion").GetString());
    }
}
