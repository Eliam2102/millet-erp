using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Tesoreria;

/// <summary>
/// 86e3yxq6n · las firmas de Tesorería las da quien tiene la facultad, no quien tiene cierto puesto:
/// cierre de conciliación y autorización del pago a cuenta (otra persona que quien lo capturó).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class FirmasPorFacultadTests(WebApplicationFactory<Program> factory)
{
    private static readonly Guid Empresa = AdjuntosProveedorAmbiente.EmpresaInicialId;

    [Fact]
    public async Task Cerrar_conciliacion_con_puesto_Jefe_pero_sin_la_facultad_responde_403()
    {
        await using var amb = new AdjuntosProveedorAmbiente(factory);
        var (sinFacultad, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.TesoreriaConciliacionOperar);

        var r = await sinFacultad.PostAsJsonAsync(
            $"/api/v1/tesoreria/conciliaciones/{Guid.NewGuid()}/cerrar",
            new { firmanteNombre = "Rodrigo Medina Solís", firmantePuesto = "Jefe de Almacén" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Autorizar_pago_a_cuenta_quien_lo_capturo_se_rechaza_y_otra_persona_con_la_facultad_firma()
    {
        await using var amb = new AdjuntosProveedorAmbiente(factory);
        var permisos = new[] { PermisosCanonicos.TesoreriaPagosCuentaAutorizar, PermisosCanonicos.TesoreriaDocumentosGestionarTodasSucursales };
        var (capturista, capturistaId) = await amb.ClienteConPermisosAsync(permisos);
        var (otro, _) = await amb.ClienteConPermisosAsync(permisos);
        var (sinFacultad, _) = await amb.ClienteConPermisosAsync(PermisosCanonicos.TesoreriaDocumentosGestionarTodasSucursales);

        Guid movimientoId;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            var n = Guid.NewGuid().ToString("N");
            var cuenta = new CuentaBancaria(Empresa, "BBVA Egresos", n[..12], null, "MXN");
            db.CuentasBancarias.Add(cuenta);
            var pago = MovimientoBancario.RegistrarPagoACuenta(Empresa, cuenta, Guid.NewGuid(), 18_450m,
                DateOnly.FromDateTime(DateTime.Today), $"FIRMA-{n[..8]}", null, "Flete urgente sin factura", capturistaId, DateTimeOffset.UtcNow);
            db.MovimientosBancarios.Add(pago);
            await db.SaveChangesAsync();
            movimientoId = pago.Id;
        }
        var url = $"/api/v1/tesoreria/pagos-cuenta/{movimientoId}/autorizar";

        Assert.Equal(HttpStatusCode.Forbidden, (await sinFacultad.PostAsync(url, null)).StatusCode);

        var propio = await capturista.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, propio.StatusCode);
        Assert.Equal("PAGO_CUENTA_MISMO_USUARIO", await AdjuntosProveedorAmbiente.CodigoAsync(propio));

        Assert.Equal(HttpStatusCode.OK, (await otro.PostAsync(url, null)).StatusCode);

        var repetida = await otro.PostAsync(url, null);
        Assert.Equal("PAGO_CUENTA_YA_AUTORIZADO", await AdjuntosProveedorAmbiente.CodigoAsync(repetida));
    }
}
