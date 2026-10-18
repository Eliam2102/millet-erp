using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.P6;

// Recepción con CFDI por procesar (sin sucursal), tolerancia, concurrencia y F5 por API.
public sealed partial class P6SucursalEndpointsTests
{
    private static Task<HttpResponseMessage> RecibirAsync(HttpClient client, Dictionary<string, Guid> d, decimal cantidad)
        => client.PostAsJsonAsync("/api/v1/almacen/recepciones/",
            new RegistrarRecepcionConFacturaCommand(d["oc"], new(2026, 10, 9), null, Guid.NewGuid().ToString(), "DEMO 86e3pc50v",
                [new(d["articulo_p7"], d["linea_oc_p7"], cantidad, null, null, d["bin_p7"])]));

    private static async Task<JsonElement> ProblemaAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task FijarToleranciaAsync(Guid articuloId, decimal porcentaje)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        (await cat.Articulos.SingleAsync(a => a.Id == articuloId)).ActualizarToleranciaRecepcion(porcentaje);
        await cat.SaveChangesAsync();
    }

    [Fact]
    public async Task P6c_tolerancia_cero_rechaza_exceso_y_acepta_saldo_exacto()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;

        using var excede = await RecibirAsync(datos.Operativo, d, 11);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, excede.StatusCode);
        var problema = await ProblemaAsync(excede);
        Assert.Equal("RECEPCION_EXCEDE_TOLERANCIA", problema.GetProperty("code").GetString());
        Assert.Contains("(10)", problema.GetProperty("detail").GetString());

        using var exacto = await RecibirAsync(datos.Operativo, d, 10);
        Assert.Equal(HttpStatusCode.Created, exacto.StatusCode);
    }

    [Fact]
    public async Task P6c_tolerancia_diez_por_ciento_permite_recibir_hasta_el_limite()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;
        await FijarToleranciaAsync(d["articulo_p7"], 10);

        using var response = await RecibirAsync(datos.Operativo, d, 11);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task P6c_tolerancia_diez_por_ciento_rechaza_exceso_sobre_el_limite()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;
        await FijarToleranciaAsync(d["articulo_p7"], 10);

        using var response = await RecibirAsync(datos.Operativo, d, 12);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problema = await ProblemaAsync(response);
        Assert.Equal("RECEPCION_EXCEDE_TOLERANCIA", problema.GetProperty("code").GetString());
        Assert.Contains("(11)", problema.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task P6c_dos_recepciones_simultaneas_de_seis_dejan_una_201_y_una_422()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;

        var respuestas = await Task.WhenAll(RecibirAsync(datos.Operativo, d, 6), RecibirAsync(datos.Operativo, d, 6));
        try
        {
            Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Created));
            var rechazada = respuestas.Single(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);
            Assert.Equal("RECEPCION_EXCEDE_TOLERANCIA", (await ProblemaAsync(rechazada)).GetProperty("code").GetString());
        }
        finally
        {
            foreach (var r in respuestas) r.Dispose();
        }
    }

    [Fact]
    public async Task P6c_corporativo_con_OC_propia_y_bin_ajeno_es_422_sucursal_distinta()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;
        using (var scope = factory.Services.CreateScope())
        using (var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            almacen.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), datos.Ajenos["bin_p7"], d["articulo_p7"]));
            await almacen.SaveChangesAsync();
        }

        using var response = await RecibirAsync(datos.Corporativo, new Dictionary<string, Guid>(d) { ["bin_p7"] = datos.Ajenos["bin_p7"] }, 1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("RECEPCION_SUCURSAL_DISTINTA", (await ProblemaAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task P6c_lineas_recibibles_informa_maximo_diez_con_tolerancia_cero()
    {
        await using var datos = await PrepararP7ScopeAsync();
        var d = datos.Propios;

        using var response = await datos.Operativo.GetAsync($"/api/v1/almacen/recepciones/ordenes/{d["oc"]}/lineas-recibibles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lineas = await response.Content.ReadFromJsonAsync<JsonElement>();
        var linea = lineas.EnumerateArray().Single(l => l.GetProperty("lineaOcId").GetGuid() == d["linea_oc_p7"]);
        Assert.Equal(10m, linea.GetProperty("pendiente").GetDecimal());
        Assert.Equal(10m, linea.GetProperty("maximoRecibible").GetDecimal());
    }
}
