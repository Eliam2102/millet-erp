using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Web;

/// <summary>
/// UX-F3-05: los conteos por estado de cada bandeja (tabs) deben sumar el total del listado
/// con los mismos filtros (alcance por sucursal incluido) y respetar el filtro <c>q</c>.
/// </summary>
public sealed class ConteosPorEstadoBandejasTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/api/v1/almacen/recepciones")]
    [InlineData("/api/v1/almacen/salidas")]
    [InlineData("/api/v1/almacen/conteos")]
    [InlineData("/api/v1/almacen/devoluciones-proveedor")]
    [InlineData("/api/v1/cuentas-por-pagar/facturas")]
    [InlineData("/api/v1/cuentas-por-pagar/notas-credito")]
    [InlineData("/api/v1/cuentas-por-pagar/notas-cargo")]
    [InlineData("/api/v1/cuentas-por-pagar/viaticos")]
    [InlineData("/api/v1/cuentas-por-pagar/comprobaciones")]
    public async Task Conteos_suman_el_total_del_listado_con_y_sin_busqueda(string ruta)
    {
        using var admin = await LoginAsync(factory);
        foreach (var filtro in new[] { "", "q=zzz-sin-coincidencias-zzz" })
        {
            var lista = await admin.GetAsync($"{ruta}?limit=1&{filtro}");
            Assert.True(lista.StatusCode == HttpStatusCode.OK, await lista.Content.ReadAsStringAsync());
            var conteos = await admin.GetAsync($"{ruta}/conteos-por-estado?{filtro}");
            Assert.True(conteos.StatusCode == HttpStatusCode.OK, await conteos.Content.ReadAsStringAsync());

            var total = (await Json(lista)).GetProperty("total").GetInt32();
            var suma = (await Json(conteos)).EnumerateArray().Sum(x => x.GetProperty("total").GetInt32());
            Assert.Equal(total, suma);
        }
    }
}
