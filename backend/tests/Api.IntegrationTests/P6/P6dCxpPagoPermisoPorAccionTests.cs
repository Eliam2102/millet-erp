using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.P6;

// Cuentas por pagar y pagos: el permiso de la acción se exige aunque el documento sea de la propia sucursal
// (el control es por acción, no por sucursal), y quitarlo no afecta la lectura de la sucursal.
public sealed partial class P6SucursalEndpointsTests
{
    public static TheoryData<string, string, string, string> AccionesCxpPago => new()
    {
        { "factura", "PATCH", "", PermisosCanonicos.CuentasPorPagarFacturasEditar },
        { "factura", "POST", "/enviar-revision", PermisosCanonicos.CuentasPorPagarFacturasEnviarRevision },
        { "factura", "POST", "/liberar-revision", PermisosCanonicos.CuentasPorPagarFacturasLiberarRevision },
        { "factura", "POST", "/autorizar", PermisosCanonicos.CuentasPorPagarFacturasAutorizar },
        { "factura", "POST", "/cancelar", PermisosCanonicos.CuentasPorPagarFacturasCancelar },
        { "pago", "POST", "/revertir", PermisosCanonicos.TesoreriaPagosRevertir },
        { "pago", "POST", "", PermisosCanonicos.TesoreriaPagosAplicar },
    };

    [Theory]
    [MemberData(nameof(AccionesCxpPago))]
    public async Task CxP_y_pago_sin_permiso_de_la_accion_es_403_aunque_el_documento_sea_de_su_sucursal(
        string tipo, string metodo, string sufijo, string permiso)
    {
        await using var datos = await PrepararAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var permisoId = await db.Permisos.Where(p => p.Codigo == permiso).Select(p => p.Id).SingleAsync();
            db.RolPermisos.RemoveRange(await db.RolPermisos.Where(p => p.RolId == datos.RolId && p.PermisoId == permisoId).ToListAsync());
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);
        }

        // La política de permiso corta antes del enlace del cuerpo: basta un JSON vacío.
        // «pago» sin sufijo es el alta (POST /pagos): no lleva id.
        var ruta = tipo == "pago" && sufijo == "" ? Ruta(tipo, null) : Ruta(tipo, datos.Propios[tipo]) + sufijo;
        using var request = new HttpRequestMessage(new HttpMethod(metodo), ruta) { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-Expected-Version", "1");
        using var response = await datos.Operativo.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("SUCURSAL_NO_ASOCIADA", await response.Content.ReadAsStringAsync());

        // Quitar la acción no revoca la lectura de su sucursal.
        using var lectura = await datos.Operativo.GetAsync(Ruta("factura", datos.Propios["factura"]));
        Assert.Equal(HttpStatusCode.OK, lectura.StatusCode);
    }
}
