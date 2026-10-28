using System.Net;

namespace Millet.Api.IntegrationTests.P6;

// ADM-03b · prueba 3: los listados de requisiciones y órdenes de compra no muestran documentos de otra
// sucursal. Mismo fixture P7 (una RQ y una OC por sucursal; el operativo solo está asociado a la propia).
public sealed partial class P6SucursalEndpointsTests
{
    [Theory]
    [InlineData("/api/v1/compras/requisiciones?limit=500", "rq")]
    [InlineData("/api/v1/compras/ordenes?pageSize=500", "oc")]
    public async Task Listado_de_compras_de_la_sucursal_A_no_muestra_documentos_de_B(string ruta, string llave)
    {
        await using var datos = await PrepararP7ScopeAsync();
        var propio = datos.Propios[llave].ToString();
        var ajeno = datos.Ajenos[llave].ToString();

        using var operativo = await datos.Operativo.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, operativo.StatusCode);
        var textoOperativo = await operativo.Content.ReadAsStringAsync();
        Assert.Contains(propio, textoOperativo);
        Assert.DoesNotContain(ajeno, textoOperativo);

        // El corporativo (todas las sucursales) sí ve los dos: el filtro es por alcance, no por datos.
        var textoCorporativo = await datos.Corporativo.GetStringAsync(ruta);
        Assert.Contains(propio, textoCorporativo);
        Assert.Contains(ajeno, textoCorporativo);
    }
}
