using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>86e3kdwng · P8.6 no deducibles e IVA no acreditable.</summary>
public sealed class NoDeduciblesEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Cxp = "/api/v1/cuentas-por-pagar";

    [Fact]
    public async Task Catalogo_clasificaciones_lista_DEMO_con_permiso_de_captura_y_403_sin_el()
    {
        var amb = new AdjuntosProveedorAmbiente(factory, _ => { });
        var (conPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento);
        var (sinPermiso, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.CuentasPorPagarFacturasLeer);

        var ok = await conPermiso.GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var filas = (await ok.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Contains(filas, f => f.GetProperty("clave").GetString() == "COMIDAS" && f.GetProperty("descripcion").GetString() == "DEMO · Comidas");
        Assert.True(filas.Count >= 6);

        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync(Cxp + "/catalogos/clasificaciones-no-deducibles")).StatusCode);
    }
}
