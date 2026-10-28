using System.Net;
using System.Text.Json;

namespace Millet.Api.IntegrationTests.P6;

// Alcance por sucursal de recepciones de Almacén (ADR-0051). Reutiliza el fixture P7: una recepción
// por sucursal (propia y ajena) y un operativo asociado solo a la propia.
// Nota: las recepciones no exponen ruta de descarga de adjuntos (solo el blob del packing list se sube
// sin propietario); el control de adjuntos de OC está en Compras/Oc/AdjuntosOcAlcanceTests.
public sealed partial class P6SucursalEndpointsTests
{
    [Fact]
    public async Task Recepcion_de_otra_sucursal_da_403_SUCURSAL_NO_ASOCIADA_en_detalle()
    {
        await using var datos = await PrepararP7ScopeAsync();

        using var ajena = await datos.Operativo.GetAsync($"/api/v1/almacen/recepciones/{datos.Ajenos["recepcion_p7"]}");
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
        Assert.Contains("SUCURSAL_NO_ASOCIADA", await ajena.Content.ReadAsStringAsync());

        using var propia = await datos.Operativo.GetAsync($"/api/v1/almacen/recepciones/{datos.Propios["recepcion_p7"]}");
        Assert.Equal(HttpStatusCode.OK, propia.StatusCode);
    }

    [Fact]
    public async Task Listado_de_recepciones_de_la_sucursal_A_no_muestra_las_de_B_ni_las_cuenta()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var propia = datos.Propios["recepcion_p7"].ToString();
        var ajena = datos.Ajenos["recepcion_p7"].ToString();

        var operativo = await datos.Operativo.GetStringAsync("/api/v1/almacen/recepciones/?limit=500");
        Assert.Contains(propia, operativo);
        Assert.DoesNotContain(ajena, operativo);

        // Filtrar explícitamente por la OC o el sub-almacén de B tampoco la revela.
        var porOc = await datos.Operativo.GetStringAsync($"/api/v1/almacen/recepciones/?ordenCompraId={datos.Ajenos["oc"]}");
        Assert.DoesNotContain(ajena, porOc);
        var porSub = await datos.Operativo.GetStringAsync($"/api/v1/almacen/recepciones/?subAlmacenId={datos.Ajenos["sub_p7"]}");
        Assert.DoesNotContain(ajena, porSub);

        // Las pestañas de conteo por estado salen del mismo filtro: el corporativo ve al menos una más.
        async Task<int> TotalAsync(HttpClient cliente) => JsonDocument.Parse(
                await cliente.GetStringAsync("/api/v1/almacen/recepciones/conteos-por-estado"))
            .RootElement.EnumerateArray().Sum(x => x.GetProperty("total").GetInt32());
        Assert.True(await TotalAsync(datos.Corporativo) > await TotalAsync(datos.Operativo));
    }
}
