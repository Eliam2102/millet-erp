using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// La reversa de un cobro aplicado (cartera, M-237) y de la aplicación de un anticipo (M-344) piden
/// <c>cuentas_por_cobrar.aplicacion-pago.reversar</c> (solo Contador General). Los permisos que antes
/// bastaban (confirmar la aplicación de pago, liberar el apartado) ya no alcanzan.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class PermisoReversaCobroTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public async Task Sin_el_permiso_de_reversa_responde_403_aunque_tenga_los_permisos_anteriores()
    {
        var cliente = await ContabTestKit.ClienteConPermisosAsync(factory,
            PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar,
            PermisosCanonicos.FacturacionAnticiposLiberarApartado,
            PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales);
        try
        {
            foreach (var r in await LlamarAsync(cliente))
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(cliente);
        }
    }

    [Fact]
    public async Task Con_el_permiso_de_reversa_pasa_la_autorizacion_y_llega_al_caso_de_uso()
    {
        var cliente = await ContabTestKit.ClienteConPermisosAsync(factory,
            PermisosCanonicos.CuentasPorCobrarAplicacionPagoReversar,
            PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales);
        try
        {
            // Documentos inexistentes: el caso de uso responde 404, señal de que la política de permiso ya dejó pasar.
            foreach (var r in await LlamarAsync(cliente))
                Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(cliente);
        }
    }

    private static async Task<HttpResponseMessage[]> LlamarAsync(HttpClient c) =>
    [
        await c.PostAsync($"/api/v1/cuentas-por-cobrar/cartera/correcciones/{Guid.NewGuid()}/autorizar", null),
        await c.PostAsJsonAsync($"/api/v1/cuentas-por-cobrar/cartera/correcciones/{Guid.NewGuid()}/rechazar", new { Motivo = "Prueba de permiso" }),
        await c.PostAsJsonAsync($"/api/v1/facturacion/anticipos/{Guid.NewGuid()}/reversar-aplicacion",
            new { FacturaVentaId = Guid.NewGuid(), Motivo = "Prueba de permiso" }),
    ];
}
