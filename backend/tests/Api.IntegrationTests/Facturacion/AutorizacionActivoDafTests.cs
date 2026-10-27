using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Facturacion.Domain.Ports;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>P11: ejecutar solamente con PostgreSQL desechable. El lector de activo usa una muestra explícita.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class AutorizacionActivoDafTests(WebApplicationFactory<Program> factory)
{
    [Theory]
    [InlineData(true, HttpStatusCode.Created)]
    [InlineData(false, HttpStatusCode.Forbidden)]
    public async Task DAF_autoriza_y_contador_solo_registra(bool daf, HttpStatusCode esperado)
    {
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IActivosFijosReadPort>();
            s.AddSingleton<IActivosFijosReadPort, ActivoMuestra>();
        }));
        // La prueba unitaria del seed comprueba que estos son los permisos de cada rol.
        var cliente = await ClienteConPermisosAsync(app, daf
            ? PermisosCanonicos.FacturacionActivosAutorizar : PermisosCanonicos.ActivosFijosBajasRegistrar);
        try
        {
            using var respuesta = await cliente.PostAsJsonAsync("/api/v1/facturacion/activos/autorizar",
                new { activoRef = "P11-DEMO-LOCAL", precioVenta = 500, motivo = 5,
                    solicitadoPor = "Área DEMO LOCAL", evidencia = "Factura DEMO LOCAL" });
            Assert.Equal(esperado, respuesta.StatusCode);
        }
        finally { await LimpiarClienteAsync(cliente); }
    }

    private sealed class ActivoMuestra : IActivosFijosReadPort
    {
        public Task<ActivoFijoLectura?> ObtenerAsync(string referencia, CancellationToken ct) =>
            Task.FromResult<ActivoFijoLectura?>(new(referencia, "Activo DEMO LOCAL P11", 400, 100, false));
    }
}
