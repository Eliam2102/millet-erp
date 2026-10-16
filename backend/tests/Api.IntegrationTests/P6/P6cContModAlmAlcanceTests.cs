using System.Net;
using System.Net.Http.Json;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Conteos;

namespace Millet.Api.IntegrationTests.P6;

// Alcance por sucursal de conteos, saldos, reportes, devoluciones internas, cierre y F5 de salidas (ADR-0051).
public sealed partial class P6SucursalEndpointsTests
{
    [Fact]
    public async Task P6c_conteo_de_sucursal_ajena_queda_fuera_del_alcance_del_operativo()
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var creado = await datos.Corporativo.PostAsJsonAsync("/api/v1/almacen/conteos/",
            new CrearConteoCommand(TipoConteo.Rotativo, new(2026, 10, 9), datos.UsuarioId, datos.Ajenos["sub_p7"], null));
        Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        var conteoId = (await creado.Content.ReadFromJsonAsync<CrearConteoResponse>())!.ConteoId;

        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.GetAsync($"/api/v1/almacen/conteos/{conteoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.PostAsync($"/api/v1/almacen/conteos/{conteoId}/iniciar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.PostAsync($"/api/v1/almacen/conteos/{conteoId}/aprobar", null)).StatusCode);
        Assert.DoesNotContain(conteoId.ToString(), await datos.Operativo.GetStringAsync("/api/v1/almacen/conteos/?limit=500"));
    }

    [Fact]
    public async Task P6c_operativo_crea_conteo_en_su_sucursal_y_sin_sucursal_es_403()
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var propio = await datos.Operativo.PostAsJsonAsync("/api/v1/almacen/conteos/",
            new CrearConteoCommand(TipoConteo.Rotativo, new(2026, 10, 9), datos.UsuarioId, datos.Propios["sub_p7"], null));
        Assert.Equal(HttpStatusCode.Created, propio.StatusCode);

        using var sinSubAlmacen = await datos.Operativo.PostAsJsonAsync("/api/v1/almacen/conteos/",
            new CrearConteoCommand(TipoConteo.Rotativo, new(2026, 10, 9), datos.UsuarioId, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, sinSubAlmacen.StatusCode);
    }

    [Fact]
    public async Task P6c_saldos_de_sucursal_ajena_son_403_y_el_listado_general_no_la_incluye()
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var ajeno = await datos.Operativo.GetAsync($"/api/v1/almacen/saldos/?subAlmacenId={datos.Ajenos["sub_p7"]}");
        Assert.Equal(HttpStatusCode.Forbidden, ajeno.StatusCode);
        Assert.DoesNotContain(datos.Ajenos["sub_p7"].ToString(), await datos.Operativo.GetStringAsync("/api/v1/almacen/saldos/?limit=500"));
    }

    [Fact]
    public async Task P6c_reporte_mp_cnk_de_sucursal_ajena_es_403()
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var respuesta = await datos.Operativo.GetAsync($"/api/v1/almacen/reportes/mp-cnk?subAlmacenId={datos.Ajenos["sub_p7"]}");
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task P6c_devolucion_interna_con_vale_ajeno_es_403()
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var respuesta = await datos.Operativo.PostAsJsonAsync("/api/v1/almacen/devoluciones-internas",
            new AplicarDevolucionInternaCommand(datos.Ajenos["vale_p7"], datos.Propios["sub_p7"], new(2026, 10, 9), "Integro", "DEMO P6c", null,
                [new DevolucionInternaLineaInput(Guid.NewGuid(), 1, datos.Propios["bin_p7"])]));
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task P6c_cierre_de_mes_como_operativo_es_403()
    {
        await using var datos = await PrepararP7ScopeAsync();
        using var respuesta = await datos.Operativo.PostAsJsonAsync("/api/v1/almacen/cierre-mes", new EjecutarCierreMensualCommand(2026, 10));
        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task P6c_salida_con_RQ_propia_y_bin_ajeno_del_corporativo_es_422_sucursal_distinta()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;
        RegistrarSalidaLineaInput[] salidas = [new(d["articulo_p7"], d["linea_rq_p7"], 1, null, null, null, null, datos.Ajenos["bin_p7"])];
        using var respuesta = await datos.Corporativo.PostAsJsonAsync("/api/v1/almacen/salidas/",
            new RegistrarSalidaConRequisicionCommand(d["rq"], new(2026, 10, 9), null, "DEMO P6c", salidas));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        var problema = await ProblemaAsync(respuesta);
        Assert.Equal("SALIDA_SUCURSAL_DISTINTA", problema.GetProperty("code").GetString());
    }
}
